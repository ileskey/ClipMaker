# Phase 1 4a-3c-3c-3a fragment decode anchor 관찰

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `7cff6e6b22443aa176689d511b3cad9abd11b3d2`, branch `phase/1`, clean. [동일 파일 track 연결](PHASE_1_STAGE_4A3C3C2_LINK_RESULT.md) 커밋을 확인했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **bounded moof/tfhd/tfdt 관찰·동일 파일 probe 반환 연결 구현/관련 검증 완료**. 4a-3c-3c-3 codec/fragment/native 증거 단위 중 fragment 관찰을 먼저 구현했다. codec configuration·trun/trex sample timing·edit/priming native 변환·source O/FPS gate는 아직 남아 있다.

## 관찰과 제한

Mp4TimingReader의 file 결과를 internal `Mp4FileTimingObservation(Initialization, FragmentAnchors)`로 확장했다. pure ParseMoov의 기존 init timing 결과는 유지했다. InitialReferenceProbe는 같은 file read lock/pre·posthash 안에서 얻은 anchors를 원래 reference·init timing·explicit track links·Demux 관찰과 반환한다. public 계약/사용자 채택 정책은 변경하지 않는다.

- top-level moof의 actual file offset, 각 traf의 explicit tfhd TrackId·24-bit HeaderFlags, optional tfdt BaseDecodeTime을 보존한다. tfdt v0 unsigned32/v1 unsigned64는 ulong으로 유지하며 Int64 overflow를 wrap/truncate하지 않는다. native signed timestamp로 변환 가능한지는 후속 gate의 책임이다.
- tfhd는 version0의 prefix/ID만 해석한다. optional field lengths/default values·trun/mfhd/trex/mvex·mdat payload/sample offsets는 이번에 검증하지 않는다. HeaderFlags를 보존하지만 decode/presentation/codec 의미를 확정하지 않는다. unknown box 내용도 건너뛴다.
- tfdt 부재는 null이다. 없는 값을0이나 이전 fragment end로 채우지 않는다. moof 없는 입력의 anchor 목록은 빈 관찰이다. 여러 moof에서 같은 track이 다시 나오는 것은 보존하며 decode-time이 뒤로 가는 경우도 연속성/불연속 정책으로 자동 변환하지 않는다.
- 같은 moof에서 track ID가0/중복이거나 tfhd가 누락/중복, tfdt 중복/잘못된 body 길이, traf 없는 moof, moov에 없는 foreign track ID는 InvalidMetadata/Data=null이다. tfhd/tfdt 범위 밖 version과 tfdt nonzero reserved flags는 UnsupportedFormat이다. tfdt v0/v1의 body 길이는 각각8/12bytes다.
- moof body당2MiB·전체 moof body 합계8MiB·최대1024 moofs·moof당16 trafs, 모든 moof에서 해석하는 nested boxes 합계10,000으로 제한한다. 기존 moov2MiB/top-level10,000/initialization nested10,000 제한은 별도로 유지한다. media payload는 seek로 건너뛰며 partial 성공을 반환하지 않는다. input stream 소유/cleanup은 caller 책임이다.

tfdt가 decode clock이라는 구분은 [FFmpeg mov.c의 tfdt/trun 읽기 구현](https://github.com/FFmpeg/FFmpeg/blob/n8.0/libavformat/mov.c#L5305)을 대조했다. 이 primary 구현을 참고해 관찰을 분리했으며 규격 전체 검증기를 구현했다는 뜻은 아니다. tfdt/start_pts/최초 decoded PTS가 같아도 source O/native correction0·AAC priming·CFR·coverage·decoder 준비에 대한 Verified fact를 만들지 않는다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| Downloader Infrastructure tests `--no-restore` | 최종 exit0, 통과467/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |
| 보존된 로컬 복사본 + 실제 ToolLocator/ProcessRunner/FFprobe8.1 | Succeeded,213 frames·2 track links·4 decode anchors |

새15개 사례는 v0/v1 unsigned 최대값·offset/track/flags·read-only 목록·missing/empty anchor·반복 track/뒤로 가는 값 보존·foreign/duplicate/missing/short fragment·unknown versions·moof 단일/누적/개수 제한·동일 bound reference 반환을 검사했다. 첫466개가 통과했고 probe 반환 사례를 추가한 최종467개도 통과했다. 테스트/빌드 실패나 새 경고는 없다.

실제 실행은 이전 단위에서 소유한 ignored `.artifacts/initial-probe-real/<GUID>/initial.mp4`를 재사용했다. reflection harness로 local fixture reference/hash를 구성하고 sandbox 밖에서 실제 adapter를 실행했다. 파일 bytes는2,050,886이며 원본·복사본 모두 변경/삭제하지 않았다. HTTP collector/selected source service 전체 실행·network·다운로드·설치·새 프로젝트 생성은 없다. fixture range/provenance label은 source 범위/선택 지원의 증거가 아니다.

| moof offset | track ID | HeaderFlags | BaseDecodeTime |
|---:|---:|---:|---:|
|1240|1|131104|12000|
|1240|2|131128|95935|
|1026310|1|131104|18000|
|1026310|2|131128|144063|

이는 해당 파일의 container decode anchor 관찰이다. mediaTimescale1/6000·1/48000인 track 연결을 유지하지만 sample별 PTS·유효 sample 지원·gap/coverage를 재구성한 결과는 아니다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

## 다음 단위·커밋

다음 **4a-3c-3c-3b**는 codec/header·sample timing과 skip/priming 관찰 및 native 판정의 미확인/미지원 gate다. trun/default sample timing을 확인하지 않은 tfdt anchor만으로 source O를 발행하지 않는다. 이후 초기 source 원점/FPS inspector·target acquisition 연결과 전체 지원 검증이 남는다. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.

이번 커밋 범위는 Infrastructure 기존2·기존 test1/신규 test1·결과/설계/source 계약/baseline4문서다. staging/commit은 하지 않았다. 권장 메시지: `feat: Observe bounded MP4 fragment decode anchors`.
