# CHZZK playlist·fragment 원점 근거 대조

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `2175903d15da76c533fb31bf2e00246084b4bddf`, branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

## 판정

[이전 일반화 실험](PHASE_1_CHZZK_GENERALIZATION_EXPERIMENT.md)의 stream minimum 반례를 playlist와 실제 초기 fragment까지 확인했다. 두 품질의 playlist 시간·프로그램 날짜는 일치하지만 첫 media packet과 fragment decode 시각은 다르다. **이 자료만으로 사용자 IN에 대응하는 품질 독립적인 원본 시간 0을 확정하지 않는다.** playlist 경계 시각을 각 품질의 첫 packet에 직접 붙이는 방식도 같은 내용의 시간을 달리 만드는 문제가 있다.

획득 실패와 초기 음성 coverage 문제의 실험상 우회·보완은 기존 결과대로 유지한다. 원본 시간 기준 해결·4단계 시작 준비·제품 구현 완료와 구별한다.

## 범위·수행

기존 CHZZK video `15430558`의 hls-8384(1080p60), hls-3192(720p60)를 대상으로 했다. yt-dlp 2026.08.19·ffprobe 8.1·.NET SDK 10.0.302를 그대로 사용했다.

1. 기존 메타데이터 서비스를 통해 variant URL·header를 메모리에서만 사용했다. 두 media playlist를 BCL HttpClient로 조회해 tag 개수, 선언값, EXTINF 누적 시간, PROGRAM-DATE-TIME, discontinuity, map 유무만 저장했다.
2. 각 품질에서 index 0·1·45의 세 segment만 내려받았다. playlist 누적 구간은 각각 `[0,2)`, `[2,4)`, `[90,92)`다. `.m4v` URI에 EXT-X-MAP이 있었고, init+fragment를 로컬 파일로 조합했다. 전체 VOD는 내려받지 않았다.
3. 로컬 ffprobe의 packet PTS/DTS·payload SHA256을 추출했다. 별도 MP4 atom reader로 mdhd/mvhd·tfdt·tfhd·mfhd 및 elst/sidx 존재를 확인했다. near-IN packet은 이전 원본 참조와 대조했다.
4. 공개 video API의 시간 관련 필드를 확인했다. 추가 미디어 없이 playlist 날짜 선언과 DATERANGE 속성 이름을 보완 조회했다.

playlist/init 조회는 각각 2MiB, media segment는 16MiB, HTTP 각 단계·probe는 60초 제한이다. 암호화·byte range가 있으면 segment 조합을 진행하지 않는 harness다. 실제 두 playlist에는 해당 tag가 없었다. 저장한 여섯 조합 파일의 합계는 **7,578,771 bytes**이며 이 수에는 파일마다 반복한 init bytes가 포함된다. 원격 실제 전송량 합계로 해석하지 않는다.

실험 harness는 `.artifacts/phase1-chzzk-origin-20261011-01` 아래에만 만들었다. 새 NuGet 패키지·도구를 설치하지 않았다. 최초 `--no-restore` 빌드는 assets 미생성으로 실패했고, 로컬 RestoreSources를 지정한 sandbox 빌드도 실패했다. 승인된 동일 로컬 복원·빌드 및 후속 보완 빌드는 최종 경고/오류 0으로 성공했다. 여섯 probe 모두 exit 0·root/descendant 종료·stream drain·완전한 stdout을 확인했다. 제품 코드가 바뀌지 않아 기존 제품 테스트를 반복하지 않았다. 이번에는 trim/출력 재인코딩·재생 정확도 검증을 수행하지 않았다.

## playlist 결과

두 playlist 모두 version 7, target duration 2, media sequence 1, discontinuity sequence 0, ENDLIST가 있다. PLAYLIST-TYPE 선언은 없다. segment 1,334개 모두 누적 시작·PDT·discontinuity가 두 품질에서 같다. 각 playlist의 모든 PDT 차이는 첫 PDT 대비 EXTINF 누적 시작과 일치한다. 관찰한 discontinuity tag는 0개다.

| 항목 | 1080p | 720p |
|---|---|---|
| 첫 segment PDT | 2026-09-29T07:30:11.641Z | 동일 |
| index 45 시작·PDT | 90초·07:31:41.641Z | 동일 |
| 마지막 segment 시작 | 2666초 | 동일 |
| 마지막 EXTINF | 1.561333초 | 1.563333초 |
| 전체 EXTINF 합계 | 2667.561333초 | 2667.563333초 |

DATERANGE는 ID·START-DATE만 있고 START-DATE는 `2026-09-29T07:30:10.641Z`다. 첫 PDT보다 1초 이르지만, class·명시적 media PTS/사용자 재생 offset 계약은 확인되지 않았다. 이것을 원점으로 채택하지 않는다. 공개 API의 liveOpenDate는 `2026-09-29 16:30:10`(응답 문자열에 timezone 없음), duration은 정수 2667, playback meta.duration은 2667.5613이다. publishDateAt은 게시 시각이며, watchTimeline은 null이었다. 이 필드들에서 정밀한 사용자 시간↔media PTS 변환을 확인하지 못했다.

## fragment·packet 결과

아래는 `첫 PTS − playlist 누적 시작`을 공통 48,000 clock으로 표현한 **경계 차이**다. 원점이라고 명명하거나 채택하지 않는다.

| 품질·stream | index 0 | index 1 | index 45 |
|---|---:|---:|---:|
| 1080p video | 96000 | 96000 | 96000 |
| 1080p audio | 95935 | 95167 | 95167 |
| 720p video | 100792 | 100792 | 100792 |
| 720p audio | 99007 | 99264 | 99264 |

초기 video PTS는 1080p `12000/6000`, 720p `12599/6000`다. 720p 최초 DTS/tfdt는 `12499/6000`으로 PTS와도 다르다. audio 최초 PTS/tfdt는 각각 `95935/48000`, `99007/48000`다. atom reader는 여섯 파일에서 elst·sidx를 발견하지 못했다. init의 mvhd/mdhd duration 0은 관찰값이며 원점 보정 근거가 아니다. tfdt는 실제 track별 decode 시작을 담고 있어 그 자체로 공통 사용자 원점을 지정하지 않는다.

초기 두 segment의 공통 AAC payload 184개는 품질 간 PTS 차이가 0 sample 94개·+1 sample 90개다. index 45의 공통 AAC payload 90개도 모두 +1 sample이다. 같은 playlist index/PDT에 들어간 첫 AAC는 품질별로 서로 다른 내용이다. index 45에서 양쪽 각각 214개 packet 전부가 이전 원본 참조의 stream·PTS·payload hash와 정확히 일치했다. 따라서 이번 차이를 로컬 remux가 만든 shift로 설명할 수 없다.

## 해석·다음 단위

HLS 규격은 EXTINF를 segment 길이로 정의하고 PDT를 첫 media sample의 날짜에 연결한다. 동시에 variant 간 같은 내용의 timestamp 대응을 요구하며, 다른 playlist의 같은 sequence 번호만으로 같은 위치를 가정하지 못하게 한다. 이 규격 설명만으로 이번 provider의 nominal 경계·track별 실제 sample 경계 차이를 없앨 수는 없다. [RFC 8216 §4.3.2.1, §4.3.2.6, §4.3.3.2, §6.2.4](https://www.rfc-editor.org/rfc/rfc8216.html)

현재 자료로 배제되는 선택은 품질별 stream minimum, 품질별 first video PTS/DTS, 품질별 first audio PTS를 각각 사용자 0으로 재설정하는 방식이다. 고정 2초·1080p 우선·전체 variant 최소값·DATERANGE 날짜 기준도 검증된 계약 없이 채택하지 않는다. 게시 날짜를 재생 원점으로 사용하는 근거도 없다. 사용한 extractor는 playback URL과 duration을 조회하지만 사용자 시간↔PTS 계약을 제공하지 않는다. [yt-dlp 2026.08.19 CHZZK extractor](https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/yt_dlp/extractor/chzzk.py)

다음 논리적 단위는 **실제 CHZZK player의 재생 시간↔media timestamp 매핑 확인**이다. 첫 구간과 같은 중간 IN에서 player가 적용하는 timeline offset과 품질 변경 전후의 대응을 관찰해 현재 packet 근거에 연결해야 한다. 날짜/sequence로 추정하거나 더 많은 trim 성공으로 원점을 입증하려 하지 않는다. player 근거 확보가 불가능하면 그 제한을 기록하고, 지원 가능한 source 시간 계약·실행 거절 기준을 명시적으로 정리한다. 그 후에 공통 원점을 적용한 trim·sample/끝 경계 검증과 다른 source 대조를 진행한다.

## 증거·커밋 범위

- [전체 playlist scalar·경계·동일 payload 대조](evidence/PHASE_1_CHZZK_SOURCE_ORIGIN.json)
- [여섯 segment의 packet·atom·실행 증거](evidence/PHASE_1_CHZZK_SOURCE_ORIGIN_PACKETS.json)

서명 URL·header·cookie·raw playlist/API 응답은 증거에 포함하지 않았다. 파일명은 고정 실험 이름만 쓴다. 제품 코드·Phase 2·WPF UI·ClipEditor는 변경하지 않았다. 이번 논리적 커밋은 이 결과 문서, evidence 두 개, 설계·baseline 갱신을 함께 포함한다. staging/commit은 수행하지 않았다.
