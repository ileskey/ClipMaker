# Phase 1 4a-3c-3c-1 MP4 timing header 관찰

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `358839336e255c2a837bfdb9f35bde1beed99a29`, clean. [4a-3c-3b](PHASE_1_STAGE_4A3C3B_PROBE_RESULT.md) 커밋을 확인했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **bounded MP4 timing header reader 구현·관련 검증 완료**. 4a-3c-3c native/container 증거 검증을 논리적 커밋으로 나눈 첫 단위다. codec priming/skip·native 변환·source O/FPS 판정·inspector/acquisition 전체 구현 완료가 아니다.

## 관찰 모델과 reader

Infrastructure internal `Mp4TimingReader`와 반환 관찰을 추가했다. 기존 G4b-2b 등의 ignored Atoms 실험 코드와 문서에서 version별 offsets/별도 movie·track clocks를 확인했다. 실험용 전체 파일 읽기를 제품 구현으로 복사하지 않고, caller가 제공한 readable/seekable local stream의 top-level header만 읽고 media payload는 seek로 건너뛴다. input stream 소유권은 caller에 있고 reader는 생성/수정/삭제/close하지 않는다.

- movie `mvhd` timescale, track `tkhd` ID·`mdhd` media timescale, `edts/elst` 존재 여부와 entry 목록을 보존한다. track ID는 FFprobe stream index가 아니며 index+1로 대응시키지 않는다.
- elst v0/v1의 unsigned movie-duration ticks, signed media-time ticks, signed16 rate integer/fraction을 별도 보존한다. `media_time=-1` empty edit와 다른 negative 값·rate를 관찰 단계에서 임의 보정하지 않는다. 해당 형태가 실제 지원 가능한 native 관계인지는 별도 gate가 판단해야 한다.
- edit list 없음과 present/entry0을 구별한다. zero header duration/empty edit 등이 source end 또는 audio absence 증명이라고 처리하지 않는다. reader는 header duration/end나 codec 정보를 발행하지 않는다.
- top-level 유일 moov body≤2MiB·top-level 최대10,000 boxes, 해석하는 nested boxes 별도 최대10,000, tracks≤16·edit entries≤16이다. 32-bit/extended64/size0 box 크기와 parent 경계를 검사한다. mdat 크기대로 메모리를 할당하지 않는다.
- version0/1과 읽는 full-box의 최소 header 길이, 양수 timescale/track ID, ID 중복, 필수 timing box의 누락·중복, elst count/entry 길이, trailing byte/box bounds를 검사한다. unknown version·moov/track/edit 상한은 UnsupportedFormat, malformed header/box/I/O는 InvalidMetadata다. 취소에는 Data=null이다.
- 반환 목록은 read-only이며 raw media/URI/프로세스 출력은 노출하지 않는다. 미해석 box 내용은 건너뛴다. 이것은 MP4 전체 구조·codec·암호화·fragment sample table 검증기가 아니다. header 읽기 성공이나 edit 부재를 native 변환0/지원 성공으로 승격하지 않는다.

이번 reader는 **standalone container 관찰**이다. 아직 InitialReferenceProbe의 같은 read lock/hash 수명에 결합하지 않았다. caller가 임의 stream을 넘겨 성공했다는 사실을 collected reference의 byte identity 증거로 사용해서는 안 된다. 현재 FFprobe 관찰은 계속 Demux이며 Verified/native fact는 생성하지 않는다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| Downloader Infrastructure tests `--no-restore` | 최종 exit0, 통과432/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

새16개 테스트는 synthetic box streams로 v0/v1 clocks·unsigned64 duration/signed64 media time·empty/absent edit·rate 보존·read-only 목록·extended/size0 box·누락/중복/zero/short header·edit/moov/box 상한·취소를 검증한다. 4MiB fake mdat는 seek로 건너뛰고 실제 읽은 header/moov bytes가1024미만임을 검사했다. network·실제 source 파일·FFprobe 실행·다운로드/설치는 이번 단위에서 없다.

첫428개 실행 중1개가 실패했다. 추가 경계 사례를 포함한432개에서도 동일 실패가 재현됐다. malformed top-level 입력의 InvalidDataException이 ReadAsync에서 결과로 변환되지 않고 전파된 문제였다. 예외 catch 경계를 명시적으로 수정한 최종432개가 모두 통과했다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

## 다음 단위·커밋

다음 **4a-3c-3c-2**는 동일 수집 파일/lock/hash 수명에 container 관찰을 결합하고 explicit track ID로 FFprobe stream과 대조하는 단계다. codec/fragment native 증거와 미지원/미확인 경계를 계속 분리해야 한다. edit·priming 관계를 확인하기 전에 start_pts/decoded 첫 PTS/playlist0을 source O로 채택하지 않는다. source origin/FPS inspection·target acquisition·4a 전체/Phase 전체는 남아 있다.

이번 커밋 범위는 Infrastructure1·test1·결과/설계/source 계약/baseline4문서다. staging/commit은 하지 않았다. 권장 메시지: `feat: Read bounded MP4 timing header observations`.
