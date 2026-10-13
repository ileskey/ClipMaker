# Phase 1 4a-3c-2 HLS 참조 수집·소유 파일

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `4953a7b44bec453387899d9cc02cf85bcdbd230f`, branch `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-3c-2 bounded 참조 수집·파일 수명 구현/관련 검증 완료**. [4a-3c-1](PHASE_1_STAGE_4A3C1_HLS_RESULT.md)의 plan을 HTTP reader와 새 소유 파일에 연결했다. 실제 CHZZK 다운로드·native 원점/FPS/coverage 증명·inspection/acquisition service·4a-3c 전체/Phase 완료는 아니다.

## 구현과 증명 범위

Infrastructure internal `HlsReferenceCollector`를 추가하고 playlist에 실제 조회 body의 SourceBytes를 보존했다. reader의 응답 직후와 collector의 응답 수락 전에 취소를 검사하도록 보완했다. 기존 byte/HTTP retry·query/URI/public 계약은 바꾸지 않았다.

- selected leaf URI·playlist ID·init URI가 동일해야 한다. 선택 segment 목록을 같은 playlist와 requested playlist range에서 다시 계산해 foreign/edited plan을 거절한다. 실제 whole-reference span도 budget 안에 있어야 한다.
- alreadyReadBytes는 최소 playlist.SourceBytes를 포함하고 전체 budget 이하여야 한다. init부터 선택한 segment 순서대로 요청하며 **전체 누적 body bytes**를 검사한다. 남은 byte budget이0이면 다음 HTTP를 보내지 않고, 요청마다 reader32MiB cap도 적용한다. init/segment empty·누락·초과·불완전 응답을 건너뛰지 않는다.
- existing work directory 아래 generated file-ID basename으로 FileMode.CreateNew·FileShare.None을 사용한다. 디렉터리를 자동 생성하지 않고 caller 경로/미디어 URI로 파일명을 만들지 않는다. 기존 충돌 파일은 변경/삭제하지 않는다.
- 각 참조의 complete bytes만 파일에 append하고 완전한 계획의 write/flush/close가 끝나야 candidate를 반환한다. SHA-256·실제 파일 bytes·누적 budget·요청/실제 playlist ranges·playlist ID·OwnedMediaFile/선택 format provenance를 반환한다. 이 확장자/hash/byte-completeness는 native/container/clip 성공 증명이 아니다.
- 실패/취소에는 Data=null이다. 이번 호출이 CreateNew로 소유한 partial만 close 뒤 정리한다. 원래 실패/Cancelled는 cleanup 실패로 덮지 않고 RemainingFiles/안전한 warning을 반환한다. close가 불확실하면 delete를 시도하지 않는다. 그렇지 않으면 소유 파일 하나만 삭제하며 user/기존 파일과 부모 폴더는 건드리지 않는다.
- otherwise-complete 후보의 close 실패는 OutputFailed이고 candidate를 발행하지 않는다. 처음 CreateNew 실패는 소유 증거가 없으므로 삭제하지 않는다. 실제 작업 결과에 잔존 파일을 연결하는 adapter/workspace 책임은 후속 구현이며 내부 result를 public OperationResult로 바꾸면서 warning/경로를 버려서는 안 된다.

생산 경로의 storage는 FileStream/CreateNew와 File.Delete다. write/close/cleanup fault 주입용 delegate는 trusted test/composition 의존성이며 source input에서 받지 않는다. reader/client와 작업폴더 전체의 소유 수명은 호출 adapter/workspace가 관리한다. 아직 native 검사 snapshot/MediaInput을 만들지 않는다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore` | 최종 exit0, 통과385/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

새12개 사례를 포함한다. fake HTTP의 init/segment8 bytes를 실제 fixture 파일에 수집해 순서·파일 내용·SHA/byte budget·소유/provenance를 대조했다. playlist budget 누락·budget 소진·missing/empty 참조·취소·user 파일 보존·CreateNew 충돌·cleanup/write/close 실패·foreign/edited plan·span budget을 검사했다.

최초381개는 통과했으나 추가한 cancel+close-failure 교차 사례에서384개 중1개가 실패했다(예상 Cancelled, 실제 Failed). 응답 후/수락 전 취소를 검사해384개를 통과했고, complete 후보의 close-failure 차단 사례를 추가한 최종385개가 모두 통과했다. 이 실패/수정은 파일 수명/HTTP 경계 결과이며 실제 source 실패를 숨긴 것이 아니다.

fixture는 저장소 내부 고유 `.artifacts/hls-collector-tests/<GUID>`에 tiny fake bytes/fixture user 파일만 생성하고 해당 고유 폴더의 파일을 정리했다. 실제 HLS media·외부 network/CDN·FFmpeg/ffprobe/yt-dlp·설치 작업은 없다. Core/Media.Infrastructure 전체 테스트는 이번에 합산하지 않았고 WPF는 솔루션 빌드만 검사했다.

## 다음 단계·커밋 범위

다음 **4a-3c-3**는 수집한 초기 source 참조의 native/container/stream 관찰과 원점·FPS 증거 연결이다. source inspector의 public 결과를 만들 때 actual identity·scope·movie/native 의미와 파일 ownership/잔존 자원을 보존해야 한다. playlist 좌표0을 source O로 채택하거나 미확인 decoder history·target coverage를 Verified로 만들지 않는다. user/source→playlist 변환·target acquisition·최종 처리/검사는 여전히 남아 있다.

실제 bounded CHZZK 검증은 source/native 관찰 경로가 연결된 뒤 수행한다. byte candidate 수집 구현 완료와 그 사이트/포맷의 지원·정확도 검증 완료를 구별한다. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.

이번 커밋은 Infrastructure 신규1/기존2파일·test1파일·결과/설계/source 계약/baseline4문서다. 공백·문서 링크·변경 범위를 확인했다. staging/commit은 하지 않고 기존 사용자/실험 자료를 보존했다. 권장 메시지: `feat: Collect bounded HLS references into owned files`.
