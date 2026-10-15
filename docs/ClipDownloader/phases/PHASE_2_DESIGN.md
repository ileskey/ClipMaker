# ClipDownloader Phase 2 상세 설계

작성일: 2026-10-15 (Asia/Seoul).

상태: **Completed / Passed**. WPF MVP 구현·필수 검증을 완료했고 Completion은 `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`, 완료 태그는 `phase-2-complete`다. 검증된 제품 소스는 `86fd9f8014aba1135fe30ba6d5b14be4df3ab152`이며 논리 단위별 실제 결과·설계 보완·최종 검증 및 커밋 해시 대응은 [구현·검증 기록](PHASE_2_IMPLEMENTATION.md)을 따른다. 아래 현재 구조와 미구현 설명은 상세 설계 작성 당시의 시작점 기록이다. 기존 Phase 1 정책의 재승인을 요구하지 않는다.

## 1. 목표와 범위

기존 WPF 호스트에 단일 클립 생성 흐름을 연결한다. URL 조회, Start/End `HH:MM:SS:FF`, Quality, Fast/Accurate, 명시 MP4 출력 경로와 파일 선택, 준비 요약과 사용자 확인, 실행·진행 상태·취소·최종 결과가 대상이다.

Phase 3 상세 metadata/thumbnail, Phase 4 queue/병렬 실행, Phase 5 player, 로컬 입력 UI, ClipEditor, 새 media 지원 전략, 자동 도구 설치는 제외한다. 제목·길이·선택 FPS/화질 등 현재 작업 확인에 필요한 최소 정보는 사용한다.

이 context는 설계를 담당한다. 제품 코드·프로젝트·패키지·도구 설치·영상 다운로드를 설계의 일부로 실행하지 않는다.

## 2. 기준과 시작 조건

기준 문서:

- [설계 인계](PHASE_2_DESIGN_HANDOFF.md), [운영 규칙](../PHASE_DESIGN.md), [Baseline](../PHASE_BASELINE.md)
- [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처](../requirements/02_ARCHITECTURE.md), [구현 계획](../requirements/03_IMPLEMENTATION_PLAN.md)
- [전체 구조](../PROJECT_STRUCTURE_DESIGN.md), requirements의 Codex/저장소 지침
- [Phase 1 최종 인계](PHASE_1_FINAL_VERIFICATION_HANDOFF.md), 상세 설계와 단계별 검증 기록, [Phase 0 완료 인계](PHASE_0_COMPLETION_HANDOFF.md), [CLI 사용법](../../../apps/ClipDownloader.Cli/README.md)

| 항목 | 실제 확인 및 해석 |
|---|---|
| Phase 1 Completion / 제품 소스 기준 | `4509f54dadbf140f787bdfd4a16dcc2b1f964c48` |
| 완료 tag | `phase-1-complete`의 dereferenced commit이 Base와 일치 |
| 설계 시작 HEAD / branch | `d40f70d377e1e74489f4df356089fdc339ccc947` / `phase/1` |
| Base 이후 HEAD 변경 | 인계 관련 docs 6개만 변경. 제품 소스 변경 없음 |
| 선행 검증 | 기존 기록에 build 경고/오류 0, 총 1195개 회귀 통과 및 양 사이트·양 모드 사용자 재생 확인. 이번 설계에서는 재실행하지 않음 |
| 작업 트리 | 최초 status는 clean. 읽기 조사 도중 외부 변경으로 병합 중 상태 관찰. 아래 별도 기록 참조 |
| Phase 2 실제 Base / branch (Git 준비 시 갱신) | `3cbb04ed09df07d75dc5b963905b497909d8daeb` / `phase/2` |

2026-10-15 Git 준비 재확인: 사용자가 생성한 `phase/2`를 그대로 사용한다. 현재 시작점 `3cbb04e`는 `phase/1` 병합 commit이며 Phase 1 Completion을 ancestor로 포함한다. Completion과의 diff는 인계 문서 6개뿐이고 제품 소스는 같다. 위 ‘설계 시작’ 항목은 최초 조사 시점의 이력이다. 구현 기능은 아직 추가하지 않았다.

Phase 1의 제한된 지원 범위를 유지하는 WPF 연결 설계는 진행할 수 있다. 일부 CHZZK MPD metadata 실패, Accurate 먼 위치의 source-start 참조 한계, playlist 2 MiB/계획 segment 64개 및 획득 예산, codec/clock 증거 범위, NativeEnd Unknown을 UI에 전달해야 한다. 이를 임의 입력·구간 지원 보장으로 해석하지 않는다.

### 최초 조사 도중 관찰한 병합 상태와 후속 정리

최초 조사 당시 HEAD는 `d40f70d`이며 `MERGE_HEAD`는 `0759dc52475538df848f765ce30c005cf755107f`였다. 당시 `git status --short`에서 다음 충돌을 확인했다.

- `UU ClipMaker.slnx`
- `AA docs/ClipDownloader/PHASE_DESIGN.md`
- `AA src/ClipMaker.Media.Infrastructure/ClipMaker.Media.Infrastructure.csproj`
- `AA tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj`

최초 설계에서는 충돌을 수정하지 않고 별도 설계 문서만 작성했다. 이후 `3cbb04e`에서 병합이 완료됐고, 후속 Git 준비 요청 시 위 4개 파일에 충돌 표식이 미커밋 변경으로 다시 존재했다. 각 diff가 표식과 과거 대안 내용의 추가뿐임을 확인하고 원본을 ignored `.artifacts/phase2-git-prep`에 보관했다. 표식을 제거한 내용이 현재 HEAD와 동일함을 파일별 비교한 뒤 정리했다. 병합을 재실행하거나 reset/history 변경을 하지 않았다. 아래의 실제 준비 검증 기록과 Phase 1의 과거 검증 결과를 구분한다.

## 3. 현재 구조와 변경 계획

현재 소스에는 WPF/CLI 호스트 2개, 라이브러리 4개, 주요 회귀 테스트 프로젝트 3개가 있다. 최초 조사 때의 병합 충돌 표식은 후속 Git 준비에서 정리했다.

| 영역 | 실제 역할과 Phase 2 영향 |
|---|---|
| `apps/ClipDownloader` | 기존 MainViewModel은 도구 파일 탐색만 제공. 입력·작업 상태와 명령을 추가할 대상 |
| WPF Views | 현재 도구 상태 창. 클립 입력·요약·결과 화면과 Closing 이벤트 연결을 설계 |
| WPF Composition / App | 도구 탐색 DI와 Serilog 소유. 실행 서비스 등록 및 await 가능한 종료 절차 필요 |
| `apps/ClipDownloader.Cli` | 등록과 Application 호출 순서 참고. WPF가 CLI 실행 파일을 호출하지 않음 |
| `ClipDownloader.Application` | metadata·포맷 선택·Prepare/Execute·진행/결과의 기존 계약을 사용 |
| Downloader Infrastructure | URL·yt-dlp·선택 원본 획득·출력 workspace. ViewModel에서 직접 사용하지 않음 |
| Media Core / Infrastructure | 정확한 시간 모델과 처리/검증. UI에서 대체 계산하거나 지원 gate를 완화하지 않음 |

Application 계약은 현재 확인한 것만 사용한다. DI 및 테스트 배치는 제10~11절을 따른다.

## 4. 실제 연결 계약

- `IVideoSourceParser`: 공식 지원 URL 판별.
- `IVideoInfoService.GetVideoInfoAsync(Uri, CancellationToken)`: `OperationResult<VideoInfo>` 반환.
- `FormatSelector.Select(VideoInfo, QualityOption)`: metadata 선언 기반 선택. 실제 실행 지원 보장이 아님.
- `IClipJobService.PrepareAsync(ClipRequest, VideoInfo, CancellationToken)`: 쓰기 없는 `OperationResult<PreparedClip>` 반환. 도구 실행 진단을 포함하므로 비동기 작업으로 취급.
- `ExecuteAsync(PreparedClip, IProgress<JobProgress>?, CancellationToken)`: 재검사·획득·처리·확정·정리 후 `ClipJobResult` 반환. 같은 PreparedClip은 한 번만 실행.
- `PreparedClip`: 선택 snapshot, Start/End, nullable Range, FinalPath, Budget, Warnings, PendingChecks, 제목/업로더. 확인 요약의 원천.
- `JobProgress`: JobId, Stage, nullable Percent, Message. `Completed` 단계는 성공 판정이 아님.
- `ClipJobResult`: Status, Error, OutputPath, Warnings, RemainingTemporaryPaths, Presentation. 성공에만 확정 출력 경로가 있으며 실제 표시 범위는 Presentation으로 전달.

현재 파일은 `Models/ClipJobModels.cs`에 여러 모델이 함께 정의돼 있다. 초기 설계의 개별 파일 예시를 실제 경로로 간주하지 않는다.

## 5. 첫 설계 항목 — 상태와 비동기 수명

### 5.1 상태의 책임

화면 상태를 다음 세 축으로 관리하는 안을 사용한다. 중복 bool은 각 상태에서 파생한다.

- metadata: `Empty / Loading / Ready / Failed`
- 작업: `Idle / Preparing / AwaitingConfirmation / Executing / Cancelling / Completed / Failed / Cancelled`
- 앱 수명: `Running / Closing / Closed`

실행 내부 단계는 별도 `JobProgress.Stage`로 표시한다. Downloading/Processing 등의 알림마다 작업 상태 enum을 바꾸지 않는다. 도구 파일 탐색은 독립 상태를 유지하되 실행과 충돌하는 새 사용자 작업은 아래 명령 규칙으로 막는다.

### 5.2 상태 전이와 명령 활성 조건

| 현재 조건/입력 | 처리와 다음 상태 |
|---|---|
| Running, 비작업 상태에서 영상 불러오기 | URL parser 검사 후 metadata Loading. 이전 metadata·준비 결과를 즉시 무효화하고 비동기 조회 |
| 현재 조회 성공 | metadata Ready, 작업 Idle. 새 metadata에 대한 선택/입력 검증 |
| 조회 실패/취소 | 실패는 metadata Failed, 취소는 Empty. 이전 영상 metadata를 사용 가능 상태로 복구하지 않음 |
| Ready이고 알려진 입력 오류 없음, 다운로드 클릭 | 입력/metadata revision을 고정해 ClipRequest 생성, 작업 Preparing. 중복 클릭 차단 |
| 현재 Prepare 성공 | 반환된 PreparedClip을 보관, AwaitingConfirmation. 요약·주의·미확인 검사 표시 |
| 준비 실패/취소 | Failed/Cancelled. 준비 객체 폐기, 입력 수정 또는 재시도 허용 |
| 요약에서 실행 확인 | revision·JobId 일치와 미실행 여부를 확인하고 Executing. 저장한 동일 PreparedClip을 한 번만 전달 |
| 요약에서 돌아가기 또는 입력 수정 | 준비 객체 폐기, Idle. 재실행에는 새 Prepare 필요 |
| 실행 중 취소 | token 취소 요청, Cancelling. Execute task와 정리 종료를 계속 await |
| Execute 반환 | 최종 Status에 따라 Completed/Failed/Cancelled. 경고·잔존 경로·실제 범위도 함께 보관 |
| 재실행 | 사용자가 다시 다운로드하면 항상 새 Prepare. 이전 PreparedClip 재사용 금지 |

입력 편집은 비작업 상태와 AwaitingConfirmation에서 허용한다. Preparing/Executing/Cancelling 및 metadata Loading에서는 입력·파일 선택·재조회·준비·실행을 잠근다. Loading/Preparing에서는 취소를 제공한다. Running인 동안에만 새 작업을 시작할 수 있다.

취소 요청 후 실제 작업이 끝나기 전에는 잠금을 풀지 않는다. Finalizing 이후 늦은 취소는 성공으로 끝날 수 있으므로 UI에서 결과를 Cancelled로 덮어쓰지 않는다. ProcessTerminationFailed는 일반 실패와 별도로 후속 실행을 잠그고 프로세스 종료 미확인을 알린다. 단순 파일 삭제 경고만으로 동일한 잠금을 적용하지 않는다.

명령 guard는 CanExecute뿐 아니라 호출 진입점에서도 검사한다. 수명/작업 enum 외에 필요한 session 실행 잠금, dialog 활성 여부, 경로 검사 대기는 독립 사실로 유지하고 임의의 IsBusy bool로 대체하지 않는다.

| 명령 | 실행 가능 조건 |
|---|---|
| LoadMetadata | Running, 도구 초기 탐색 종료, dialog 없음, Loading/Preparing/Executing/Cancelling 아님. 새 조회는 이전 확인을 폐기 |
| Prepare(다운로드) | 위 비작업 조건과 metadata Ready, 유효 선택/알려진 입력 오류 없음/경로 검사 완료, AwaitingConfirmation 아님, session 실행 잠금 없음 |
| ConfirmExecute | AwaitingConfirmation, revision 일치, 동일 PreparedClip 미실행, Running, dialog 없음, session 잠금 없음 |
| BackFromConfirmation | AwaitingConfirmation이며 Running. 준비 폐기 후 Idle |
| BrowseOutput | Running, dialog 없음, Loading/Preparing/Executing/Cancelling 아님 |
| Cancel | Loading 또는 Preparing 또는 Executing, 취소 미요청, Finalizing/Cleaning 수신 전 |
| RefreshTools | Running, dialog 없음, 도구 탐색 없음, Loading/Preparing/Executing/Cancelling 아님 |
| AcknowledgeAndClose | Closing이며 활성 task 종료, 미확인/잔존 경고를 표시 중 |

FPS 미확인의 보류 검사는 ‘알려진 입력 오류’가 아니다. 취소 응답 대기 중 metadata 상태가 Loading으로 남아도 동일 CTS의 요청 여부로 Cancel을 잠근다. session 잠금 상태에서도 metadata 조회/입력 확인은 가능하지만 클립 Prepare/Execute는 막는다.

### 5.3 입력과 준비 자료의 관계

편집 가능한 문자열과 불변 PreparedClip을 구분한다. URL·Start·End·quality·mode·output 변경 시 입력 revision을 증가시키고 확인 가능 상태를 해제한다. metadata 교체에는 별도 revision을 사용한다.

URL이 달라지면 표시 metadata·선택·준비 결과를 무효화한다. Start/End/output 변경은 현재 영상 metadata를 유지하면서 재검증한다. quality 변경은 선택한 포맷의 FPS로 타임코드 범위를 다시 검사한다. mode 변경도 준비를 무효화한다.

확인 버튼은 보관된 PreparedClip과 입력/metadata revision이 모두 일치할 때만 활성화한다. 확인 화면의 값은 편집용 문자열에서 재구성하지 않고 PreparedClip에서 읽는다.

### 5.4 비동기 결과와 progress

각 작업에 generation과 활성 Task/CTS를 보관한다. 새 호출 허용 여부는 await 전에 UI thread에서 바꾼다. 늦은 metadata/Prepare 결과는 generation·revision·앱 수명이 일치할 때만 반영한다. Execute 결과와 자원 정리는 UI 표시 유효 여부와 관계없이 끝까지 관찰한다.

UI thread에서 만든 `Progress<JobProgress>`로 Dispatcher에 전달하되 현재 JobId·generation·활성 실행 여부를 확인한다. 최종 결과 수신 시 진행 수신을 닫아 큐에 남은 progress가 최종 상태를 덮지 못하게 한다. Percent가 없으면 불확정 표시, 있으면 해당 단계 진행률로만 표시한다. 단계 비율을 임의 합산하지 않는다.

명령과 종료 절차가 같은 활성 Task를 await할 수 있게 보관한다. CTS는 해당 작업 종료 후 dispose한다. UI에서 Wait/Result/동기 process 대기를 사용하지 않는다. 예상 오류는 구조화된 결과로 표시하고 예기치 않은 예외도 관찰해 안전한 실패 상태로 전환한다. 예외를 성공·일반 취소로 숨기거나 raw stderr/URL을 로그에 복제하지 않는다.

### 5.5 창 닫기와 소유 자원

현재 `Closing → Stop() → OnExit에서 DI dispose`는 도구 탐색용이다. 다운로드 작업에 그대로 적용하면 소유자 수명을 보장할 수 없다.

종료 설계안:

1. 첫 Closing을 보류하고 앱 수명을 Closing으로 바꾼다. 새 명령과 중복 종료 진입을 막는다.
2. 확인 대기 중인 PreparedClip은 폐기한다. 도구 탐색·metadata·Prepare·실행의 활성 token에 취소를 요청한다.
3. 창에 취소·정리 중 상태를 유지하고 활성 Task를 비동기로 기다린다. 반복 닫기로 대기/정리를 생략하지 않는다.
4. 반환된 최종 결과를 반영한다. 종료 미확인·잔존 경로가 있으면 창을 유지하고 결과를 확인할 수 있게 한다. 이후 사용자 닫기는 경고를 확인한 종료로 처리하되 자원 정리 성공으로 기록하지 않는다.
5. await할 작업이 끝나면 실제 Close를 한 번 허용한다. DI owner 정리 → 종료 로그 → logger/SelfLog 정리 순서를 유지한다.

종료 대기에 임의 timeout을 두고 프로세스가 살아 있는 상태에서 자동 dispose하지 않는다. OS 강제 종료나 프로세스 자체 강제 종료에서 정상 정리를 보장한다고 주장하지 않는다. owner Dispose의 실제 동작과 경고 확인 방식은 제10.4절을 따른다.

### 5.6 선택 이유와 대안

실행 중 편집을 잠그면 한 작업의 입력·진행·최종 결과 관계가 명확하고 queue 없는 MVP에 적합하다. 실행 중 다음 작업을 편집하는 방식은 별도 draft/result 연결이 필요하므로 현재는 채택하지 않는다. 확인 전에는 수정할 수 있고 수정 즉시 준비를 무효화한다.

확인 요약은 메인 창 내부 영역을 사용한다. 스크롤 가능한 주의·미확인 검사를 보여주고 준비 자료와 입력의 변경을 함께 관리하기 쉽다. 별도 모달은 확인을 강제하기 쉽지만 닫기·취소와 별도 창 수명이 추가된다.

## 6. 설계 진행 순서

한 항목씩 구체화하고 문서에 반영한다.

1. 상태·비동기 수명·준비 자료 무효화: 제5절.
2. 입력/validation: 제9절.
3. 화면·구성·종료: 제10절.
4. 구현 순서·검증: 제7절 및 제11절.

구현 작업은 별도 지시를 받은 뒤 진행한다. 제9~11절은 제5절 초안에서 남겨둔 화면·DI·종료 상세 결정을 구체화하며 상충 시 이 상세 규칙을 적용한다.

구현 순서는 병합 상태 확인 → 상태/수명 기반 → metadata·입력 검증 → Prepare/확인 → Execute/결과 → 통합 검증이다. 파일 책임 및 논리 단위는 제11절을 따른다.

## 7. 완료 조건과 검증 계획

이번에는 docs와 소스·Git을 읽어 계약을 대조했다. build/test/WPF 실행/다운로드는 수행하지 않았다. 아래는 구현 후 수행할 계획이다.

| 검증 | 확인할 관찰 |
|---|---|
| 상태/중복 호출 | 연속 클릭에도 조회·Prepare·Execute 각각 중복 진입 없음 |
| 준비 무효화 | 모든 관련 입력 변경 후 과거 준비 결과 실행 불가, 새 Prepare 필요 |
| 늦은 비동기 결과 | 취소·창 닫기 후 metadata/Prepare 및 큐에 남은 progress가 현재 상태를 덮지 않음 |
| 취소/확정 경합 | 정리 끝까지 대기, Finalizing 이후 결과 유지, 실패와 취소 구분 |
| 종료 수명 | 반복 닫기에도 활성 작업과 정리 완료 전 DI/logger 종료 없음 |
| 입력/오류 | 시간/소수 FPS/FPS 누락·URL·metadata·도구·output·예산 실패와 안전한 안내 |
| 실제 WPF | UI 응답, 키보드/파일 선택/확인, 실제 취소와 창 닫기, 종료 후 소유 자원 확인 |
| 실제 media | 양 사이트·양 모드의 지원 범위에서 소리·움직임 있는 10~30초 클립 생성 및 재생/동기 확인 |
| 회귀 | 기존 1195개 기준과 CLI/Phase 0 탐색·로그 수명 유지, 실제 build/test 결과 기록 |

상태 검증은 fake 서비스로 경합을 제어하고 실제 실행 검증은 별도로 한다. fake 성공과 Phase 1의 기존 증거로 WPF 통합 완료를 선언하지 않는다.

## 8. 제약과 인계

- 최초 병합 충돌 및 후속 충돌 표식은 Git 준비 시 정리했다. 구현 시작 시 작업 트리와 Base 이후 변경을 다시 확인한다.
- 입력/확인/오류 화면 상세, DI 등록 및 테스트 프로젝트 배치는 제9~11절을 따른다.
- FPS 미확인 시 Prepare.Range가 null인 계약을 유지한다. UI가 임의 30fps를 적용하거나 기존 실행 검사를 생략해서는 안 된다.
- source inspection/download/resource query는 같은 SelectedSourceAcquisitionService instance를 사용해야 한다.
- 현재 CLI 기본 64 MiB/60초는 원본 참조 획득 예산이다. 출력 길이 상한으로 안내하거나 UI 편의를 위해 확대하지 않는다.
- 현재 설계는 기존 제한을 UI에서 전달하는 범위다. MPD·먼 위치 Accurate 지원을 확대하려면 별도 명세 영향 분석이 필요하다.
- 기존 Application 계약은 유지한다. 후속 Phase는 구현·실제 검증이 끝난 결과를 사용해야 하며 현재 설계만으로 새 동작이 제공된다고 가정하지 않는다.

## 9. 입력과 validation 상세 명세

### 9.1 초기값·조회·metadata

초기값은 URL/output 빈 값, Start/End 모두 `00:00:00:00`, Quality Best, Mode Fast다. 사용자가 종료 시간을 지정해야 하며 전체 영상 길이를 기본 범위로 넣지 않는다.

URL은 `IVideoSourceParser.Parse(string?)`로 검사하고 반환된 정규화 source를 metadata 요청과 연결한다. UI에서 호스트/영상 ID를 중복 parsing하지 않는다. Loading 중에는 편집·재조회를 잠그고 취소를 제공한다. 같은 URL 재조회도 이전 metadata와 준비를 즉시 무효화한다.

성공 시 제목·업로더·길이·선택 FPS/해상도를 최소 정보로 표시한다. 누락 값은 ‘미확인’이다. Completed 이외 상태나 양의 duration 부재는 준비를 막고 이유를 표시한다. 실패/취소 후 과거 영상 정보를 현재 정보처럼 남기지 않는다.

새 metadata 성공 시 Start/End를 0으로 초기화하고 Best를 선택한다. 사용자가 지정한 output과 Mode는 유지하고 재검증한다. 이전 최종 결과는 당시 source/구간/JobId가 고정된 ‘이전 작업 결과’로 구별한다. 새 입력으로 이전 결과의 제목·구간을 바꾸지 않는다.

### 9.2 시간 입력

Start/End 각각 HH/MM/SS/FF 네 TextBox를 사용한다. HH와 FF는 최소 두 자리 표시이며 두 자리 최대 길이 제한을 두지 않는다. 24시간 이상을 허용한다. MM/SS는 두 자리, 00~59다. 편집 중 빈 값이나 한 자리는 허용하되 검증 오류로 준비를 막는다. 포커스 이탈 시 유효한 한 자리 ASCII 숫자만 두 자리로 표시한다. 잘못된 값·overflow를 0으로 바꾸거나 자르지 않는다. key filtering 대신 binding 검증으로 붙여넣기·IME·키보드 접근을 유지한다.

네 필드를 연결해 `TimecodeInput.TryParse`를 호출한다. FPS가 있으면 `Timecode.TryCreate`, `MediaTimeRange.TryCreate/TryValidateDuration`을 사용해 `Start >= 0`, `End > Start`, `End <= Duration`, `FF < 실제 FPS`를 검사한다. `[Start, End)`와 정확한 값 객체 비교를 유지하며 double 계산을 도입하지 않는다.

30000/1001의 FF는 00~29, 60000/1001은 00~59다. 시계 시간 + FF/FPS 표기이며 SMPTE/drop-frame으로 표시하지 않는다. FPS는 유리수를 보존하고 근삿값은 표시용으로만 사용한다.

FPS 미확인 시에도 네 필드를 편집할 수 있다. 구문·overflow·MM/SS 범위만 검사하고 FPS 의존 FF 상한·구간 순서·길이/전체 길이 비교는 ‘실행 중 검사 대기’로 표시한다. `PreparedClip.Range=null`을 허용하는 기존 흐름을 유지한다. 임의 FPS로 계산 길이를 넣지 않는다. 확인 후 실행 검사에서 실패할 수 있음을 요약에 표시한다. UI 전용 FPS probe나 확인 전 원본 획득은 추가하지 않는다.

quality 변경으로 선택 FPS가 달라지면 입력 숫자를 유지해 재검증하고 FPS 변경을 알린다. 프레임 수를 자동 환산하지 않는다.

### 9.3 Quality·Mode

Quality는 Best와 metadata의 양수 Height를 가진 영상 후보들의 서로 다른 높이를 내림차순으로 제공한다. VideoStream=Absent 후보는 제외한다. 실제 선택은 각 QualityOption에 대한 기존 `FormatSelector.Select`를 사용한다. selector 실패는 오류로 표시하고 준비를 막는다. 임의 format ID 입력은 제공하지 않는다.

선택 옆에 실제 video/audio format ID·FPS·해상도·코덱을 요약한다. metadata 후보 존재는 해당 모드의 실행 지원 보장이 아니다. Mode에 따라 quality를 임의 변경하거나 Fast/Accurate를 자동 전환하지 않는다. Fast 실제 경계 차이와 Accurate 참조/비용 한계를 안내한다.

### 9.4 Output과 파일명

최종 MP4 전체 경로 한 칸과 ‘찾아보기’를 제공한다. WPF는 fully qualified 절대 경로를 요구한다. 상대 경로는 명확한 입력 오류로 처리하며 CLI의 상대 경로 지원은 바꾸지 않는다. 기존 폴더·파일명·예약 이름·확장자·기존 파일 충돌은 `IOutputPathValidator.Validate`를 재사용한다. 쓰기 권한 및 경쟁 조건은 Prepare/Execute/Commit 검사 결과를 따른다. UI 검증용 파일이나 폴더를 생성하지 않는다.

앱 내부 `IOutputFileDialog`는 선택된 절대 경로 또는 취소(null)를 반환한다. WPF 구현은 MainWindow owner의 SaveFileDialog, MP4 필터, 확장자 추가, 기존 폴더 확인을 사용한다. 덮어쓰기 승인을 제공하지 않고 선택 후 validator로 기존 파일을 거절한다. 취소하면 기존 output을 유지한다. dialog가 열린 동안 다른 작업 시작을 막는다.

dialog 계약은 제안 파일명과 기존 output을 초기값으로 받는 동기 `ShowSaveFileDialog` 형태로 두고 UI thread에서 호출한다. owner는 dialog를 여는 시점에 WPF 구현에서 `Application.Current.MainWindow`로 얻는다. 생성자에서 MainWindow를 주입해 `MainWindow → ViewModel → dialog → MainWindow` 순환 의존성을 만들지 않는다. 반환 경로는 비동기 검증을 거친 뒤 준비 가능 상태에 반영한다.

제안 이름은 `{title}_{start}_{end}.mp4`다. title이 없으면 영상 ID를 쓴다. 시간 콜론은 `-`로 바꾼다. 제목의 Windows 금지/제어 문자는 `_`로 바꾸고 끝 공백/마침표를 제거한다. 예약 stem은 앞에 `_`를 붙이고 빈 값은 `clip`으로 한다. 제목 부분은 surrogate pair를 깨지 않게 최대 80 UTF-16 code unit으로 줄인다. 전체 경로 유효성·쓰기 성공은 별도 검증하며 긴 경로 지원을 보장하지 않는다.

기본 이름 정리는 앱 내부 순수 helper `OutputFileNameSuggestion`으로 둔다. 사용자가 입력한 완성 경로는 조용히 고치지 않고 필드 오류로 안내한다. 기본 output은 빈 값이며 사용자가 명시적으로 선택한다. 저장 설정 영속화는 추가하지 않는다.

### 9.5 검증 시점·접근성

기존 MVVM Toolkit의 `ObservableValidator`/`INotifyDataErrorInfo`로 필드 오류와 오류 요약을 연결한다. 문자열·시간·선택 검사는 편집 즉시 수행한다. 파일 시스템 경로 검사는 worker에서 약 300ms debounce 후 수행하고 generation으로 늦은 결과를 무시한다. 검사 중에는 준비를 막는다. Prepare에서 동일 경로를 다시 검사한다.

시간 오류는 Start/End와 구성 필드를 구분한다. 서비스 Field가 Minutes처럼 주체를 구분하지 않으면 잘못된 한쪽 필드에 귀속하지 않고 ‘구간 입력’ 요약에 표시한다. 색상 외 텍스트, label 연결, AutomationProperties.Name/HelpText를 제공한다. 사용자 명령 실패 시 첫 해당 필드/오류 요약으로 focus를 옮기고 편집 중에는 강제로 이동하지 않는다.

## 10. 화면·DI·종료 상세 설계

### 10.1 화면과 확인

URL/조회 → 최소 metadata → Start/End·Quality·Mode → Output → 준비/확인 → 진행/결과 순서로 한 창에 배치한다. resize와 세로 스크롤을 지원한다. 기존 도구 탐색은 접을 수 있는 ‘도구 상태’ 영역에 유지하며 파일 발견 검사라는 범위를 명시한다.

‘다운로드’는 Prepare만 시작한다. 준비 성공 후 메인 창 내부 확인 영역에 아래 내용을 표시하고 ‘위 설정으로 실행’/‘돌아가기’를 제공한다. 확인 버튼은 default button으로 설정하지 않아 Enter 입력이 준비와 실행을 연속 승인하지 않게 한다. 확인 영역의 시작 부분으로 focus를 이동한다.

| 확인 항목 | 데이터 |
|---|---|
| 대상 | source/영상 ID·제목·업로더·metadata 길이 |
| 선택 | 요청 quality와 실제 video/audio format, 해상도·FPS·코덱·음성 선언 |
| 요청 | Start/End, FPS 확인 시에만 계산 길이, Mode |
| 출력 | PreparedClip.FinalPath |
| 예산 | 원본 참조 64 MiB/60초, 출력 길이 상한이 아니라는 안내 |
| 주의/미확인 | PreparedClip.Warnings/PendingChecks와 선택 snapshot 경고. 중복만 제거 |

입력 수정 즉시 준비 확인을 무효화하고 다시 준비해야 한다고 표시한다. 요약은 보관된 PreparedClip에서 읽는다. 확인 전 source/workspace/output 파일을 만들지 않는다. 도구 실행 진단·기존 앱 로그는 이 금지 대상과 구분한다.

### 10.2 Progress와 최종 결과

stage를 한국어 이름으로 표시하고 고정된 요청 구간/JobId를 유지한다. Percent는 해당 단계의 0~100 범위 값만 사용하고 누락·비정상 수치는 불확정 표시한다. 단계 변경 시 이전 비율을 초기화한다. 전체 비율·남은 시간을 추정하지 않는다. UI가 Finalizing/Cleaning을 받으면 추가 취소 버튼을 잠그며 최종 결과는 계속 await한다.

성공은 최종 ClipJobResult로만 판단한다. 요청·실제 video·실제 audio 범위를 각각 사용자 원점의 정확한 유리수 초 `[Start, End)`로 표시한다. 근삿값을 추가할 수 있지만 원값을 대체하지 않는다. 임의 FPS로 실제 범위를 timecode로 바꾸지 않는다. audio=null은 ‘음성 범위 없음’이다. SourceEndVerified=false는 ‘원본 전체 끝 미확인’으로 표시한다.

결과 상세에 삽입 zero/제거 overlap/priming skip/decoded tail sample 수를 표시한다. 성공에도 warning/잔존 경로가 있으면 별도 경고로 보여준다. 실패/취소에는 임의 확정 출력 경로를 만들지 않는다. 경고·잔존 로컬 경로는 복사 가능하게 제공하며 UI가 직접 temp 파일을 삭제하지 않는다. 마지막 결과 하나만 유지하고 이력 저장/queue는 추가하지 않는다.

| 오류 | 안내와 조치 |
|---|---|
| InvalidInput/UnsupportedSource/InvalidRange | 필드 또는 구간 오류. 수정 후 새 Prepare |
| InvalidOutputPath/OutputConflict | 기존 폴더의 다른 MP4 파일명 선택. 덮어쓰기 없음 |
| MissingTool/실행 실패 | 도구 상태·안전한 원인 확인. Found와 실행 가능성 구분 |
| SourceUnavailable/InvalidMetadata | 완료 영상 여부 확인·재조회. 일부 metadata 실패를 사이트 전체 미지원으로 확대하지 않음 |
| UnsupportedFormat/UnknownFrameRate/VerificationFailed | 현재 선택/구간의 필수 검사 실패. 자동 quality/mode 변경 없음 |
| budget/segment 상한 | 안전한 Field/Message의 필요한 범위·상한 표시. 전체 다운로드 fallback 없음 |
| TimedOut/ProcessFailed/OutputFailed | 실패 단계·안전한 메시지. 재시도는 새 Prepare |
| ProcessTerminationFailed | 종료 미확인 경고와 현재 세션 클립 실행 잠금 |
| 정리 실패/파일 handle 미확인 | 원래 결과 유지·경고/잔존 경로 표시. 프로세스 종료 미확인과 구분 |

예상하지 못한 Execute 예외는 자원 상태가 불명확할 수 있으므로 클립 재실행을 잠근다. 안전한 메시지/JobId를 표시하고 임의 삭제를 하지 않는다. metadata/Prepare 예외는 해당 task가 종료된 후 재입력을 허용한다. 모든 예외를 일반 취소/성공으로 변환하지 않는다.

### 10.3 DI와 비동기 경계

WPF Composition에 CLI의 실제 실행 구성 요소를 직접 등록한다. 공통 DI 프로젝트 추출은 하지 않는다. 호스트 2개의 로그/입력 수명이 다르고 현재 중복을 위한 새 경계가 불필요하다. CLI 코드는 유지하고 WPF DI smoke로 구성 누락을 검증한다.

| 구성 | 수명/정책 |
|---|---|
| ToolPathSettings/ToolSearchEnvironment/IToolLocator | 기존 singleton, 앱 tools/PATH snapshot. 명시 사용자 설정은 기존처럼 비어 있음 |
| IProcessRunner/IToolExecutionDiagnostics/IVideoInfoService/IVideoSourceParser | 앱 singleton, CLI의 실제 구현 |
| IOutputPathValidator/IOutputWorkspace | singleton. 기준 디렉터리는 시작 시 Environment.CurrentDirectory snapshot, UI는 절대 경로만 전달 |
| SelectedSourceAcquisitionService | singleton 한 번 등록. inspection/acquisition/resource access에 동일 객체 전달하고 DI가 한 번 dispose |
| IClipMediaService/IClipJobService | singleton, 소유 자원 및 이전 종료 미확인 상태 유지 |
| SourceInspectionBudget | CLI와 동일한 64 MiB/60초. UI 설정으로 노출하지 않음 |
| MainViewModel/MainWindow/IOutputFileDialog | 앱 수명. WPF dialog 구현만 window owner를 앎 |

metadata/Prepare/Execute의 동기 preamble과 path I/O도 UI를 막지 않도록 worker의 async delegate에서 서비스를 호출하고 UI에서 await한다. Progress 객체는 UI에서 미리 만든다. command 전체를 worker로 옮겨 UI 속성을 수정하지 않는다. 실행/정리는 cancellation으로 task 관찰을 중단하지 않고 반환까지 await한다.

startup 도구 파일 탐색 중에는 새 작업을 잠근다. metadata/Prepare/Execute 중 수동 재탐색도 잠근다. 진단이 끝난 뒤 Missing/실패 상태만을 이유로 영구 잠금하지 않으며 실제 호출의 실행 진단이 권위 있는 결과다. 도구 설치·경로 편집·설정 저장은 추가하지 않는다.

### 10.4 종료와 로그

MainWindow Closing/App glue는 종료 보류·await·재닫기만 담당하고 작업 취소/수명은 MainViewModel의 `ShutdownAsync()`가 담당한다. 동일한 종료 task와 실제 Close 허용 latch로 반복 닫기와 무한 보류를 막는다. 기존 OnMainWindowClose 정책을 유지하되 준비된 종료만 실제 닫기를 허용한다. OnExit에서 비동기 작업을 시작하지 않는다.

종료 미확인/잔존 경로/예상하지 못한 실행 예외가 있으면 활성 task가 끝난 뒤 창을 유지하고 ‘경고 확인 후 종료’ 및 복사를 제공한다. Closing 상태는 유지하며 새 실행은 금지한다. 이 버튼은 실행 중 정리를 생략하지 않는다. 경고 없는 정상 반환은 자동으로 닫는다.

실제 `SelectedSourceAcquisitionService.Dispose()`는 내부 HTTP 객체 dispose이며 프로세스 회수 보장이 아니다. 미디어 정리 역시 명시 Execute/cleanup 경로를 따른다. DI dispose를 복구 절차로 간주하지 않는다. 확인 후 종료해도 미확인 자원이 정상 정리됐다고 기록하지 않는다.

종료 순서는 활성 task/정리 대기 → DI Dispose → 종료 로그 → logger Dispose → SelfLog 해제다. startup 필수 초기화 실패의 기존 안내/exit 1 경로와 부분 생성 상태 처리는 유지한다.

기존 로그 위치/회전/보관과 한 번의 로그 문제 안내를 유지한다. 추가 로그는 작업 종류·JobId·stage 전환·최종 status·error code/category/field로 제한한다. raw stderr/JSON·URL·인증값·도구 인자·사용자 경로를 추가로 복제하지 않는다. 외부 예외 object의 무조건 로깅도 피한다. 결과 화면의 잔존 경로 표시는 자동 수집 로그와 구분한다.

## 11. 구현 파일·단위와 검증 보완

### 11.1 변경 파일 책임

| 영역 | 책임 |
|---|---|
| MainViewModel.cs | 상태/명령, revision/generation/Task/CTS, 조회/Prepare/Execute·결과·ShutdownAsync |
| ViewModels/TimecodeInputViewModel.cs | Start/End 네 필드 편집·검증·값 객체 변환 |
| 필요한 준비/결과 표시 모델 | 안전한 읽기 전용 표시. 불필요한 서비스 계층 추가 금지 |
| Dialogs/IOutputFileDialog.cs, OutputFileDialog.cs | 파일 선택 계약/owner 연결 |
| OutputFileNameSuggestion.cs | 기본 이름 정리 순수 로직 |
| MainWindow.xaml(.cs), App.xaml.cs | binding/접근성/스크롤, Closing glue·로그 수명 |
| Composition/ServiceRegistration.cs | 실행 서비스 등록/수명 |
| tests/ClipDownloader.Tests | 구현 시 추가할 net10.0-windows WPF 호스트 테스트 프로젝트 |

현재 Infrastructure.Tests는 net10.0이므로 WPF 참조를 추가하지 않는다. 별도 Windows 테스트 프로젝트 하나에서 VM/DI/Dispatcher 경합을 검증하고 기존 xUnit/VSTest 버전을 재사용한다. dialog는 fake interface로 검증하며 실제 조작은 통합 검증으로 분리한다. UI thread가 필요한 검증은 STA/Dispatcher를 명시적으로 구성한다. 새 MVVM/테스트 패키지를 도입하지 않는다.

### 11.2 구현 순서

아래는 논리 단위/commit 후보다. 설계 요청만으로 commit이나 구현을 실행하지 않는다.

1. 병합 완료와 HEAD/Base 차이·구현 branch 확인, 새 제품 변경이 있으면 계약 재대조.
2. 상태/명령·task/CTS·ShutdownAsync 기반과 제어 가능한 fake 경합 검증.
3. metadata·시간/quality·output dialog/제안 이름·비동기 경로 검사·접근성.
4. Prepare 확인 요약·revision 무효화·같은 준비의 한 번 실행 계약.
5. 실제 DI·Execute·progress/결과·취소/정리·창 종료 연결.
6. 솔루션/기존 회귀 및 실제 WPF/양 사이트·양 mode 생성·재생/동기/소유 자원 검증, 결과 기록.

UI가 실행 버튼을 노출하기 전에 필요한 DI와 명령 guard를 함께 연결한다. 실제 영상 검증은 구현 요청 후 정해진 입력 범위에서 수행한다.

### 11.3 추가 검증 사례와 통과 기준

- **입력**: MM/SS=60, 음수/비ASCII/빈 값/overflow, 24시간 초과, End=Start/Duration 경계, 30000/1001 FF 29/30과 60000/1001 FF 59/60. 기존 core 검증을 복제하기보다 UI 오류 귀속·준비 차단을 검사한다.
- **FPS/quality**: FPS 변경 후 재검증, FPS=null에서 준비 허용/Range=null/경고 표시, 선택 실패와 음성 미확인. 모드를 자동 변경하지 않음.
- **파일**: dialog 취소 유지, invalid title 제안 이름, 예약 이름·기존 파일·상대 경로·폴더 부재, 검사 후 외부 파일 생성 경쟁은 Execute 거절. 검증으로 파일을 생성하지 않음.
- **경합**: 준비 후 각 입력 수정, 중복 클릭/Enter, 늦은 metadata/Prepare/progress, Finalizing 후 취소 성공, 취소 후 정리 지연, 반복 창 닫기, 부분 초기화 실패. task 종료 전 DI/logger dispose 없음.
- **결과**: stage Completed를 성공 처리하지 않음, Unknown과 요청/실제 범위·sample counts 구별, 성공+잔존 경고, ProcessTerminationFailed 후 재실행 차단, 예상 밖 실행 예외 관찰.
- **실제 WPF**: metadata/처리 중 창 이동·재그리기, 키보드와 오류 focus, 실제 파일 선택, 취소/창 닫기 후 자식 프로세스와 소유 임시 자원 확인.
- **실제 media/회귀**: 제7절의 양 사이트·양 모드 검증과 기존 1195개 기준을 보존. 새 테스트 수는 실제 결과로 기록하며 고정 통과 수를 미리 선언하지 않음.

상세 설계 단계에서는 문서 링크/계약 대조만 수행했다. 후속 Git 준비 검증은 아래에 별도로 기록한다. 실제 WPF Dispatcher 동작, 실행 가능 도구/버전, 실제 생성·재생은 구현 시작/검증 때 확인할 사항이며 현재 통과로 기록하지 않는다. 주요 UI 정책은 위 기준으로 정리했으며 실행 검증을 설계 문서만으로 대신할 수 없다.

## 12. 변경 기록

- 2026-10-15 완료 확정: `phase-2-complete^{commit}`가 `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`임을 확인하고 Completed / Passed로 갱신했다. 기존 검증 커밋과 현재 커밋의 tree 동일성을 확인했다. 이번 갱신은 문서·검증 증거의 Git 식별자 정합성만 다루며 제품 수정·build/test 재실행·태그 이동은 하지 않았다.

### Git 준비 시 실제 검증 (2026-10-15)

- `dotnet build ClipMaker.slnx --no-restore --nologo`: 성공, 경고/오류 0.
- 기존 3개 test csproj에 각각 `dotnet test --no-build --no-restore --nologo`: Core 143 / Media Infrastructure 193 / Downloader Infrastructure 859, 총 1195개 통과·실패/skip 0.
- 완료 태그 ancestry, Completion 대비 제품 소스 동일성, 문서 상대 링크, 변경 파일 공백 및 충돌 표식 부재 확인.
- 이는 Phase 2 시작점의 회귀 확인이다. 새 WPF MVP 구현·실제 UI/영상 실행 검증은 아직 수행하지 않았다.

- 2026-10-15: 기준 docs·WPF/CLI/Application 소스와 Git 상태를 대조하고 설계 시작. 첫 항목인 상태·비동기 수명 초안 작성. 조사 도중 발생한 외부 병합 상태를 별도 기록. 제품 코드·공통 기준·Git history 변경 없음.
- 2026-10-15: 사용자 상세 명세·설계 진행 지시에 따라 입력/FPS/파일명·확인/결과·DI/종료·테스트 배치·구현 순서를 구체화. 기존 서비스의 null FPS 준비 경로와 OutputPathValidator/Dispose 책임을 실제 코드로 대조. 제품 구현·build/test·다운로드는 수행하지 않음.
- 2026-10-15: 사용자 Git 준비 요청으로 기존 `phase/2`와 실제 Base `3cbb04e`를 확인. 재유입된 충돌 표식 4개 파일의 원본 보관/HEAD 동일성 비교 후 정리하고 Baseline·운영 문서에 Phase 2 시작 상태 반영. 원격 push 및 완료 태그 생성 없음.
