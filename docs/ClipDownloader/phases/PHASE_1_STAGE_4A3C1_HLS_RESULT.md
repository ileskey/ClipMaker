# Phase 1 4a-3c-1 selected HLS playlist·참조 계획

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `04ab3b990a17ec0f017ffad3d1fe6bb8319f0aa6`, 작업 branch `phase/1`, 시작 작업 트리 clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-3c-1 HLS playlist 조회/해석/참조 계획 구현·관련 검증 완료**. [4a-3b](PHASE_1_STAGE_4A3B_LOCATOR_RESULT.md)의 선택된 leaf URI와 headers를 [4a-3a](PHASE_1_STAGE_4A3A_HTTP_RESULT.md)의 bounded reader에 연결했다. fragment bytes·파일 snapshot/native 증거·실제 inspection/acquisition service·4a-3c/Phase 완료는 아니다.

## 지원한 첫 범위

Infrastructure internal 코드3파일을 추가했다. 기존 query/HTTP/public 계약·프로젝트·패키지는 변경하지 않았다. 새로운 HLS 기능을 광범위하게 지원한다고 선언하지 않는다.

- `HlsSelectedPlaylistReader`: caller가 지정한 selected HLS leaf만 bounded GET한다. metadata의 master URI로 자동 이동하거나 다른 variant/quality를 선택하지 않는다. source locator가 master를 가리키거나 leaf 대신 master가 오면 미지원으로 차단한다. 취소는 Cancelled, transport/응답 검증 실패는 안전한 기존 오류로 반환한다.
- `HlsMediaPlaylistParser`: strict UTF-8·#EXTM3U·ENDLIST·version6/7·positive TARGETDURATION·단일 initial EXT-X-MAP/URI와 complete EXTINF/segment 쌍을 검사한다. media sequence는 명시값 또는0부터 유지한다. line65,536characters·전체2MiB·segment20,000개·duration token32characters의 parser 범위를 명시 적용한다. 상한/지원 범위 밖은 실패하며 데이터 일부를 성공으로 잘라 반환하지 않는다.
- `HlsReferencePlanner`: **playlist-relative** half-open range와 budget을 입력받아 교차하는 whole segments만 선택한다. exclusive OUT에서 다음 segment를 포함하지 않고 실제 whole-reference span/count가 budget 안에 있어야 한다. init URI와 local playlist ID를 같이 넘기며 그 ID를 원격 asset generation으로 설명하지 않는다.

EXTINF는 segment duration, EXT-X-MAP은 적용 segment를 읽기 위한 initialization section의 URI를 제공한다. 이 의미와 MAP/version 조건은 [RFC 8216 §4.3.2.1](https://www.rfc-editor.org/rfc/rfc8216#section-4.3.2.1), [§4.3.2.5](https://www.rfc-editor.org/rfc/rfc8216#section-4.3.2.5)를 대조했다. 이번 parser는 표준 전체가 아닌 아래 제한된 입력 범위다.

암호화(KEY METHOD=NONE 외), BYTERANGE, 실제 DISCONTINUITY/nonzero discontinuity sequence, GAP, master/alternative rendition, LL-HLS 등 미확인 EXT 태그를 건너뛰지 않고 UnsupportedFormat으로 거절한다. map 교체/repeated map·map byterange·version 범위 밖·독립 init 없는 TS는 현재 경로로 승격하지 않는다. PROGRAM-DATE-TIME/DATERANGE는 segment를 제거/재배치하는 명령으로 사용하지 않고 source/user 공통 시계의 근거로 취급하지 않는다. 날짜 문자열·그 외 comment로 native 관계를 확인했다고 주장하지 않는다.

segment/map URI는 selected leaf를 기준으로 해석하고 HTTPS·credential/fragment/control/whitespace 부재를 검사한다. parent query/signature를 상대 child URI에 임의로 복사하지 않는다. comma가 들어 있는 quoted init URI를 지원한다. URI를 가진 internal 데이터는 일반 클래스로 두며 실패 message/ToString에 주소를 넣지 않는다.

## 좌표·budget과 증명 범위

EXTINF를 decimal text의 exact BigInteger rational로 합산하고 playlist 좌표0부터 segment ranges를 생성한다. **이0과 source O·사용자 U·native/demux PTS가 같다는 뜻이 아니다.** source inspection이 확인한 playlist↔source 관계 없이 user target을 이 planner에 직접 넣으면 안 된다. 표준/metadata duration/sequence/PDT 하나로 좌표 변환을 확인하지 않는다.

max fetched span/count는 선택한 whole reference의 시작~끝 기준이다. 작은 target이라도 두 segment에 걸쳐 실제 읽기 범위가 budget을 넘으면 거절한다. 이 단위는 fragment 실제 byte count·누적 byte budget·native coverage·decoder preroll를 아직 확인하지 않는다. parser 상한을 사용자 clip 길이/전체 제품 지원 범위로 승격하지 않으며, 이후 전략은 지원 밖 입력에 명시 오류를 반환해야 한다.

기존 [G2](PHASE_1_G2_COVERAGE_RESULT.md)의 CHZZK 한source 두format은 ENDLIST·single MAP·sequence1·discontinuity sequence0을 관찰했다. 그 기록으로 첫 후보 범위를 정했지만 원시 signed playlist를 보존/재생한 것은 아니다. 새로운 사이트 상태·playlist/native 관계의 성공을 기존 summary만으로 단정하지 않는다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore` | exit0, 통과373/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

새19개 사례가 포함된다. exact decimal sum·sequence/relative init/segment URI·signature 미복사·exclusive OUT·whole-fetch budget 초과·음수/playlist 밖 범위·지원 밖 tags/map/version·잘못된 EXTINF/target duration·dangling duration/중복 선언·unsafe URI/UTF-8/크기·selected leaf 조회와 master fallback 차단을 검증했다.

synthetic playlist/예약 example URI·fake HttpMessageHandler로 reader/parser/planner를 실행했다. **실제 CHZZK/미디어 다운로드·원본 playlist replay·native 검사 성공은 아니다.** HLS 규격 문서 조회는 수행했지만 media/yt-dlp/ffprobe/FFmpeg/설치 작업은 없었다. Core/Media.Infrastructure 전체 테스트를 합산하지 않고 WPF는 솔루션 빌드만 확인했다.

## 다음 단위·커밋 경계

다음 **4a-3c-2**는 선택된 plan의 init/segment bytes를 누적 budget·소유 파일·취소/완전성 조건과 함께 실제 source 참조로 수집하는 연결이다. 초기 source 참조를 확보한 뒤 native origin/clock·selected identity·FPS/coverage 근거를 생산해야 하며 metadata/EXTINF만으로 Verified를 만들지 않는다. user target→playlist 변환·target acquisition·최종 처리 adapter는 그 확인 순서에 따라 연결한다.

이번 커밋은 Infrastructure3파일·test1파일·결과/설계/source 계약/baseline4문서다. 공백·문서 링크·변경 범위·private 경계를 확인했다. staging/commit은 수행하지 않고 기존 사용자/실험 자료를 보존했다. 권장 메시지: `feat: Add bounded HLS playlist reference planning`.
