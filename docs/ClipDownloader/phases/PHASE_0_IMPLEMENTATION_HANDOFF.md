# ClipDownloader Phase 0 구현 context 인계

작성일: 2026-10-06 (Asia/Seoul).

상태: 이 인계 작성 당시 Phase 0 설계 확정, 구현·검증 미시작. 이후 2026-10-06 구현 context에서 구현·필수 검증을 완료했다. 최신 소스·패키지 조정·실제 결과는 [상세 설계 제17절](PHASE_0_DESIGN.md#17-구현-context의-실제-수행-결과)을 따른다. 아래 snapshot과 시작 요청문은 당시 인계 기록이며, 이 문서 자체의 작성에서는 코드를 작성하거나 프로젝트를 생성하지 않았다.

## 1. 대상과 기준 문서

대상은 ClipMaker 저장소의 ClipDownloader **Phase 0 — Solution Bootstrap 구현과 검증**이다. 이 대화는 설계 확정 담당 context였으며 실제 구현은 새 context에서 진행한다.

시작 시 아래 문서를 읽는다.

1. [확정 상세 설계](PHASE_0_DESIGN.md): 구현의 직접 기준. 특히 제8절 패키지, 제13절 파일 변경, 제14절 계약·앱 연결, 제15절 검증 절차를 확인한다.
2. [Phase 운영 규칙](../PHASE_DESIGN.md): 설계·구현 완료와 다음 Phase 시작 조건.
3. [전체 프로젝트 구조](../PROJECT_STRUCTURE_DESIGN.md): 프로젝트 경계와 참조 방향.
4. [Codex 지침](../requirements/README_FOR_CODEX.md), [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처](../requirements/02_ARCHITECTURE.md), [구현 계획](../requirements/03_IMPLEMENTATION_PLAN.md), [저장소 전략](../requirements/README_FOR_REPOSITORY.md).
5. [설계 검토 체크리스트](PHASE_0_DESIGN_CHECKLIST.md): 설계 검토 완료 기록이며 구현 체크리스트로 해석하지 않는다.

확정일은 2026-10-06이다. 사용자가 항목별 선택과 전체 설계 문서를 명시적으로 확정했다. 기존 선택을 이유 없이 다시 승인받지 않는다. 구현 증거가 설계와 충돌하면 이유·영향·최소 수정안을 기록하고 필요한 부분만 재검토한다.

## 2. 확인한 현재 저장소 상태

인계 작성 시 실제 확인한 정보:

| 항목 | 확인 내용 |
|---|---|
| 작업 디렉터리 | C:\DevelopingFiles\repositories\git\ClipMaker |
| HEAD | fc96883b18c1d6856c92ad6b029d45e095e41e69 |
| git status --short | ?? docs/ |
| 솔루션 | ClipMaker.slnx |
| 현재 프로젝트 | ClipDownloader/ClipDownloader.csproj 하나 |
| 구현 상태 | 기존 빈 WPF 템플릿. 새 라이브러리·테스트·MVVM·DI·로그·탐색 구현 없음 |
| SDK | 설계 context에서 10.0.302 설치 확인. 구현 시작 시 재확인 |
| 검증 상태 | 패키지 복원·빌드·테스트·WPF 실행·외부 도구 설치 상태 미검증 |

docs는 사용자 기준 문서와 이 context의 설계 산출물이 포함된 **미추적 파일**이다. 삭제하거나 기존 commit에 포함됐다고 가정하지 않는다. 이 작업에서는 commit을 만들지 않았다. 별도 checkout/worktree를 사용한다면 미추적 docs가 자동으로 전달된다고 가정하지 말고 실제 파일 존재를 먼저 확인한다.

현재 App은 StartupUri로 MainWindow를 열며 MainWindow는 비어 있다. csproj는 net10.0-windows, Nullable·ImplicitUsings 활성화이고 패키지·프로젝트 참조가 없다. 프로젝트 이동은 아직 수행하지 않았다.

새 context에서는 Git 상태·현재 소스·적용되는 AGENTS.md를 다시 읽고 사용자 변경을 보존한다. 이 문서의 snapshot보다 최신 소스 상태가 우선이며 차이를 기록한다.

## 3. 구현 범위와 제외 범위

기본 실행 기반만 구현한다.

- 기존 WPF 앱을 apps/ClipDownloader로 이동하고 솔루션 경로 갱신.
- 라이브러리 4개와 프로젝트 참조 경계 구성.
- 기본 MVVM, DI, 앱 시작·종료, 로그.
- 도구 경로 탐색·진단, 최소 상태 화면과 재탐색 명령.
- Media.Infrastructure.Tests 하나와 자동·수동 검증.

제외: Phase 1 상세 설계와 기능, ProcessRunner, 도구 실행·버전 확인, 타임코드·FPS 모델, 다운로드·FFmpeg 처리, CLI, 실제 영상 다운로드, 다운로드 UI, 큐, Preview, 브라우저 연동, 로컬 파일 기능, 설정 저장·로드·편집 UI, 자동 도구 설치·업데이트·동봉, 배포, ClipEditor 생성.

Media.Core는 프로젝트와 참조 경계만 만든다. 미래 기능용 빈 클래스, 범용 Common/Utils, 빈 ViewModelBase와 사용하지 않는 폴더를 추가하지 않는다.

## 4. 구현 중 반드시 보존할 결정

### 프로젝트와 UI

- 하나의 ClipMaker.slnx 안에 실행 호스트는 apps, 라이브러리는 src, 테스트는 tests에 둔다.
- 앱은 net10.0-windows, 라이브러리 4개와 테스트는 net10.0.
- 라이브러리: ClipDownloader.Application, ClipDownloader.Infrastructure, ClipMaker.Media.Core, ClipMaker.Media.Infrastructure.
- 프로젝트 참조는 상세 설계 제3절 표를 따른다. WPF의 Infrastructure 사용은 Composition으로 제한하며 ViewModel은 Application 계약을 사용한다.
- MainWindow는 Views로 이동하며 XAML과 code-behind를 ClipDownloader.Views.MainWindow로 함께 변경한다. App과 앱·어셈블리 이름은 ClipDownloader 유지.
- CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection 사용. Generic Host·Scoped 수명 없음.
- MainWindow·MainViewModel·도구 설정·탐색·진단은 Singleton.
- StartupUri 제거, DI로 창 생성. 창을 먼저 표시하고 최초 진단을 자동으로 비동기 실행.
- 화면은 앱 이름·초기화 상태·도구별 결과·다시 확인 버튼만 제공. 실행 중 버튼 비활성화와 중복 실행 금지.

### 도구 탐색

- 순서: 명시 사용자 경로 → 앱 실행 디렉터리의 tools → PATH.
- 명시 경로의 미존재·오류는 다른 위치로 자동 대체하지 않는다.
- 상대 경로는 AppContext.BaseDirectory 기준. 상세 정규화·PATH 처리 규칙은 설계 제14절을 따른다.
- 결과 상태는 **Found / Missing / SearchFailed 세 가지**다. SettingsError를 추가하지 않는다. 사용자 지정 여부·출처·검사 내역·실패 사유는 별도로 보존한다.
- 기본 후보 검사 오류 후 다음 후보를 계속 탐색한다. 나중에 발견하면 Found와 앞선 진단 내역, 미발견이고 오류가 있으면 SearchFailed, 오류 없이 미발견이면 Missing.
- 파일 존재 확인까지만 수행. 발견은 정상 실행·버전의 보장이 아니다. 도구 누락·탐색 실패가 앱 실행을 막지 않는다.
- IToolDiagnosticsService와 결과 모델은 Application, 구현은 Downloader.Infrastructure, IToolLocator와 탐색 구현은 Media.Infrastructure에 둔다.
- Media.Infrastructure는 실행 파일 이름을 받아 탐색하는 공통 계층이며 yt-dlp 전용 enum·사이트 로직을 넣지 않는다.
- File.Exists만으로 오류를 구분하지 않는다. 기본 검사 함수는 File.GetAttributes이며 오류 테스트에는 교체 함수를 사용한다.
- 설정은 읽기 전용 모델과 전달 연결만 구현한다. 기본 사용자 지정 경로는 미설정이다.

### 로그와 수명

- ILogger<T> + Serilog + File sink. Serilog 로거는 **App 소유**이며 DI 연결은 dispose: false.
- 위치: %LOCALAPPDATA%\ClipMaker\ClipDownloader\Logs.
- 날짜별·10 MiB별 분할, 최근 파일 14개 보관, Information 이상.
- 기록: 시작·종료·초기화 실패·도구 탐색 결과.
- 종료 순서: 새 탐색 차단·취소 요청 → DI 정리 → 종료 로그 → 로거 마무리. 시작 실패도 같은 정리 책임을 적용한다.
- 닫힌 화면이나 정리된 로거에 지연된 탐색 결과를 반영하지 않는다. 파일 확인의 즉시 취소를 보장하거나 종료를 무기한 기다리지 않는다.
- 탐색·진단 서비스는 로그·UI·disposable 자원을 보유하지 않고 결과를 반환한다. App/ViewModel이 수명·진단 로그를 기록한다.
- 로그 폴더·초기화 예외 및 SelfLog를 확인한다. 상세 진단은 Debug, 사용자 안내는 실행 중 한 번. 로그 실패에도 앱 실행 유지. SelfLog를 다시 Serilog로 기록하지 않는다.
- DI·창 생성 등 필수 초기화 실패는 안내·가능한 로그 후 실패 종료. 도구 누락·로그 실패와 구분한다.

### 패키지와 테스트

- 각 csproj에 정확한 버전 명시. 확정 버전 목록과 프로젝트별 배치는 상세 설계 제8·13절을 따른다. 유동 버전·중앙 버전 파일은 추가하지 않는다.
- 공식 메타데이터 기반의 설계 선택이며 실제 복원·빌드 호환성은 이번 구현에서 검증해야 한다.
- xUnit.net v3 제품군의 xunit.v3 4.0.1 + VSTest. 패키지 버전의 4를 다른 제품군으로 해석하지 않는다.
- 테스트 프로젝트 하나: ClipMaker.Media.Infrastructure.Tests.
- 임시 폴더·빈 실행 파일·주입한 기준 디렉터리/PATH 사용. 실제 설치 도구·시스템 PATH·권한 변경에 의존하지 않는다.
- 오류 테스트에는 작은 검사 함수 교체를 사용하며 파일 시스템 전체 추상화·mocking 패키지는 추가하지 않는다.

## 5. 구현 순서

1. 기준 문서와 현재 소스·변경 확인, SDK·복원 환경 확인.
2. 기존 WPF 앱 이동, 솔루션 경로 갱신, 이동 직후 빌드.
3. 라이브러리 4개와 참조 관계 구성.
4. MVVM·DI·App 시작과 종료 구성.
5. 로그와 도구 탐색·진단 구현.
6. 최소 화면과 비동기 재탐색 연결.
7. 테스트 프로젝트와 의미 있는 탐색 테스트 구성.
8. 전체 빌드·테스트·수동 실행·실패 및 종료 검증.
9. 실제 결과와 변경 내용을 상세 설계에 기록. 알려진 실패·미검증을 숨기지 않는다.

## 6. 완료 조건과 결과 기록

자동 검증의 기본 명령은 다음과 같다. 아직 실행한 결과가 아니라 구현 context에서 수행할 계획이다.

```powershell
dotnet --info
dotnet restore ClipMaker.slnx
dotnet build ClipMaker.slnx --no-restore
dotnet test tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj --no-build
dotnet run --project apps/ClipDownloader/ClipDownloader.csproj --no-build
```

빌드·테스트 성공 외에 상세 설계 제15절의 실제 동작을 확인한다.

- 창 실행·바인딩·최초 진단·재탐색·UI 응답성.
- 도구 누락 상태에서도 앱 실행.
- 명시 경로 미존재·오류, 기본 탐색 오류 후 계속 탐색.
- 로그 생성·시작과 종료·진단 기록, 분할·보관.
- 로그 초기화·SelfLog 실패의 한 번 안내와 앱 유지.
- 필수 초기화 실패의 오류 안내·실패 종료.
- 탐색 중 닫기, 결과 반영 차단·프로세스 종료·자원 정리.
- 솔루션 참조와 ViewModel의 계층 경계.

테스트용 sink·지연 함수·임시 Composition 입력은 제품 기능으로 남기지 않는다. 사용자 로그나 시스템 PATH를 변경·삭제하지 않는다. 검증 후 임시 등록을 복원하고 필요한 최종 빌드·관련 테스트를 확인한다.

설계 문서 제12절의 설계 context 수행 기록을 보존하고, 구현 context의 실제 수행 결과를 별도 날짜·소스 상태와 함께 추가한다. 구현 결과, 명령과 종료 코드, 수동 재현·관찰, 실패·미검증 사유·처리 시점, 설계 변경 근거를 기록한다. 상세 설계의 상태는 실제 작업에 맞춰 구현 중 또는 구현·검증 완료로 갱신한다.

필수 실행 검증을 수행할 수 없으면 미검증으로 기록하고 완료로 선언하지 않는다. Phase 0 구현·필수 검증 완료와 인계 기록 전에는 Phase 1 상세 설계를 시작하지 않는다. 임의 commit·push는 하지 않는다.

## 7. 새 context 시작용 요청문

아래 요청을 ClipMaker 작업 디렉터리를 사용하는 새 대화에 전달한다.

```text
ClipDownloader Phase 0의 구현과 검증을 진행해줘.

docs/ClipDownloader/phases/PHASE_0_IMPLEMENTATION_HANDOFF.md를 먼저 읽고,
PHASE_0_DESIGN.md와 연결된 운영 규칙·전체 구조·요구사항,
현재 소스와 Git 상태·적용되는 저장소 지침을 확인해줘.

Phase 0 설계는 2026-10-06에 확정됐어. 합의된 선택을 유지하면서
기본 실행 기반만 구현해줘. 기존 WPF 앱 이동, 라이브러리 4개,
MVVM·DI·로그·도구 경로 탐색·최소 화면·테스트까지가 범위야.

docs는 인계 작성 시 미추적 상태였으므로 반드시 보존해줘.
파일 이동·생성·패키지 참조와 복원은 이번 구현 범위에 포함돼.
Phase 1 상세 설계나 ProcessRunner·다운로드·타임코드·CLI·ClipEditor는 진행하지 마.

확정 설계의 순서와 완료 조건에 따라 빌드·테스트·실제 WPF 실행 및
실패·종료 검증을 수행하고, 실제 결과와 미검증 사항을 설계 문서에 기록해줘.
필수 검증을 하지 못하면 완료라고 선언하지 마.
설계 변경이 필요하면 근거와 영향을 기록하고 필요한 부분만 수정해줘.
commit이나 push는 별도 요청 없이 하지 마.
```
