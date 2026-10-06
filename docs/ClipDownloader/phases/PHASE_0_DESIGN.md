# ClipDownloader Phase 0 상세 설계

상태: 구현·필수 검증 완료 (2026-10-06, Asia/Seoul). 확정 설계를 기준으로 구현했으며 실제 결과와 최소 설계 수정은 제17절에 기록한다. 설계 context의 기록은 보존한다.

설계 작성 기준일: 2026-10-06. 설계 context에서는 구현·프로젝트 이동·패키지 설치를 수행하지 않았다. 이후 구현 context의 실제 결과는 제17절에 기록한다.

## 1. 기준과 확인한 소스 상태

- [Phase 운영 규칙](../PHASE_DESIGN.md)
- [프로젝트 구조 설계](../PROJECT_STRUCTURE_DESIGN.md)
- [Phase 0 체크리스트](PHASE_0_DESIGN_CHECKLIST.md)
- [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md)
- [아키텍처](../requirements/02_ARCHITECTURE.md)
- [구현 계획](../requirements/03_IMPLEMENTATION_PLAN.md)
- [Codex 지침](../requirements/README_FOR_CODEX.md)
- [저장소 전략](../requirements/README_FOR_REPOSITORY.md)

이 대화에서 확인한 HEAD는 `fc96883b18c1d6856c92ad6b029d45e095e41e69`다. 확인 당시 `git status --short`는 `?? docs/`였다. 소스 상태는 구현 시작 전에 다시 확인한다.

`ClipMaker.slnx`는 `ClipDownloader/ClipDownloader.csproj` 하나를 참조한다. WPF 앱은 `net10.0-windows`, Nullable과 ImplicitUsings가 활성화되어 있고 패키지·프로젝트 참조는 없다. App은 StartupUri로 빈 MainWindow를 연다. 설치된 SDK는 `10.0.302`다. 빌드·테스트·앱 실행과 외부 도구 설치 상태는 확인하지 않았다.

## 2. 확정한 목표와 범위

기존 앱을 활용하여 기본 실행 기반까지만 설계한다.

- 프로젝트 폴더 구조, 라이브러리 경계와 참조 관계
- 기본 MVVM, DI, 앱 시작·종료
- 로그와 외부 도구 경로 탐색
- 상태 표시와 재탐색을 제공하는 최소 화면
- 테스트 기반, 구현 순서와 완료 조건

다운로드 화면, ProcessRunner, 타임코드, 다운로드·미디어 처리, CLI, Preview, 큐, 설정 저장·로드·UI, 자동 도구 다운로드·업데이트, 배포, ClipEditor 생성은 제외한다. 도구 누락이나 잘못된 도구 설정은 기본 앱 실행을 막지 않는다.

설계 확정과 구현 완료는 구분한다. Phase 1 상세 설계는 Phase 0 구현·필수 검증 완료 후 시작한다.

## 3. 확정한 구조와 참조

```text
ClipMaker/
├─ ClipMaker.slnx
├─ apps/
│  └─ ClipDownloader/
├─ src/
│  ├─ ClipDownloader.Application/
│  ├─ ClipDownloader.Infrastructure/
│  ├─ ClipMaker.Media.Core/
│  └─ ClipMaker.Media.Infrastructure/
├─ tests/
│  └─ ClipMaker.Media.Infrastructure.Tests/
└─ docs/
   └─ ClipDownloader/
```

apps는 실행 호스트, src는 라이브러리를 둔다. 하나의 ClipMaker 솔루션에서 관리한다. 향후 ClipEditor는 별도 실행 앱이며 다운로더 전용 계층을 직접 참조하지 않는다. 기존 slnx 형식을 유지한다.

| 프로젝트 | 책임 | 직접 참조 대상 | TFM |
|---|---|---|---|
| ClipMaker.Media.Core | 공유 미디어 모델·계약 | 없음 | net10.0 |
| ClipMaker.Media.Infrastructure | 도구 탐색, 향후 프로세스·FFmpeg 구현 | Media.Core | net10.0 |
| ClipDownloader.Application | 다운로더 흐름·계약 | Media.Core | net10.0 |
| ClipDownloader.Infrastructure | 다운로더 설정·진단, 향후 사이트·yt-dlp 구현 | Application, 두 Media 프로젝트 | net10.0 |
| ClipDownloader | WPF 화면과 구성 | Application, Media.Core, 두 Infrastructure | net10.0-windows |
| ClipMaker.Media.Infrastructure.Tests | 도구 탐색 검증 | Media.Infrastructure | net10.0 |

WPF의 Infrastructure 사용은 Composition에 제한한다. ViewModel은 Application 계약을 사용한다. 실제 파일이 필요한 폴더만 생성하고 미래 기능용 빈 클래스는 만들지 않는다. 나머지 계층별 테스트 프로젝트는 실제 검증할 로직을 구현하는 Phase에 추가한다.

프로젝트 수를 줄이면 초기 구성은 간단하지만 계층 경계를 코드 규칙으로 관리해야 한다. 라이브러리 4개를 유지하여 프로젝트 참조로 경계를 제한한다. 일반 TFM은 다른 OS의 동작 보장이 아니며 현재 지원 OS는 Windows다.

## 4. 확정한 MVVM과 최소 화면

CommunityToolkit.Mvvm을 사용한다. MainViewModel에 속성 변경 알림과 비동기 명령을 구성하며 View code-behind에는 비즈니스 로직을 두지 않는다.

최소 화면은 앱 이름·기본 초기화 상태, yt-dlp·FFmpeg·ffprobe의 발견/누락/탐색 실패 상태, 도구 다시 확인 버튼을 제공한다. 다운로드 입력과 설정 편집은 없다.

MainWindow와 MainViewModel은 생성자 주입으로 연결한다. Composition은 등록·연결을 담당하며 화면용 상태 변환은 서비스와 ViewModel이 맡는다. 이전 대화의 Composition에서 화면용 값을 변환한다는 설명은 이 책임 배치로 정정했다.

도구 재탐색은 비동기로 실행하고 파일 확인은 UI 스레드 밖에서 수행한다. 탐색 중임을 표시하고 버튼을 비활성화하여 중복 실행을 막는다. 별도 취소 버튼은 없다.

메인 창을 먼저 표시하고 최초 도구 진단을 자동 실행한다. 초기 도구 상태는 확인 중이며 최초 탐색도 같은 비동기 명령을 재사용한다.

## 5. 확정한 DI와 앱 수명

Microsoft.Extensions.DependencyInjection 컨테이너를 사용한다. 등록과 객체 구성은 앱 Composition에 모으며 ViewModel에서 서비스 로케이터를 사용하지 않는다. Generic Host는 추가하지 않는다.

StartupUri를 제거하고 시작 코드에서 로그 초기화, DI 구성, MainWindow와 MainViewModel 생성·연결, 창 표시를 수행한다. 이후 최초 도구 진단을 실행한다.

| 대상 | 수명 |
|---|---|
| MainWindow | Singleton |
| MainViewModel | Singleton |
| 도구 설정·탐색·진단 서비스 | Singleton |
| 로깅 구성 | 앱 전체 수명 |

Phase 0에서는 Scoped 수명을 사용하지 않는다. 후속 작업 서비스의 수명은 해당 Phase에서 결정한다.

- DI 구성이나 창 생성 같은 필수 초기화 실패: 가능한 로그 기록, 오류 안내 후 실패 종료.
- 로그 초기화 실패: 파일 로그 불가를 한 번 안내하고 앱 실행은 계속. 가능한 진단은 Debug 출력에 남김.
- 외부 도구 누락·탐색 실패: 앱 실행을 계속하고 상태 표시.
- 정상 종료: 메인 창 종료로 앱 종료. 앱이 DI 컨테이너를 소유하며 컨테이너가 생성한 서비스는 컨테이너가 정리. DI 자원 정리 후 로그 마무리.

창 종료가 시작되면 새 탐색을 차단하고 진행 중 탐색에 취소를 요청한다. 탐색기는 후보 확인 사이에 취소 여부를 확인하며 종료 시작 후 결과를 화면에 반영하지 않는다. 진행 중인 파일 확인을 기다리느라 앱 종료를 무기한 지연시키지 않는다. 파일 시스템 호출 자체의 즉시 취소는 보장하지 않는다. 이 취소는 내부 수명 관리용이며 별도 취소 버튼은 추가하지 않는다. 종료 도중 작업과 자원 정리의 경합을 피하는 연결은 제14절에 기록했다.

Serilog 로거는 App이 소유한다. 상세 생성·설정은 Composition에 두되, DI 연결에는 명시적으로 전달한 로거와 dispose: false를 사용한다. 종료 시작·탐색 취소 요청, DI 컨테이너·서비스 정리, 종료 로그 기록, 로거 마무리·자원 정리 순으로 처리한다. 시작 도중 실패해도 App이 생성한 로거를 동일한 정리 경로에서 종료한다. DI 정리 실패가 로거 정리를 생략하지 않도록 최종 정리를 보장하고 중복 종료를 막는다.

DI 소유는 컨테이너가 자원 정리를 통합할 수 있으나 DI 구성 전·정리 후 로그에 별도 처리가 필요하다. 별도 수명 관리 객체는 App 코드를 줄일 수 있으나 현재 규모에서 객체와 연결 코드가 늘어난다. 이미 정한 시작·종료 순서를 직접 구현하기 위해 App 소유를 선택했다.

## 6. 확정한 로깅

서비스에는 Microsoft.Extensions.Logging의 ILogger<T>를 주입하고 Serilog와 Serilog.Sinks.File로 저장한다. 구성은 Composition 책임이다. 직접 파일 logger를 만들 때 필요한 분할·보관 관리를 기존 패키지에 맡긴다.

| 항목 | 정책 |
|---|---|
| 폴더 | %LOCALAPPDATA%\ClipMaker\ClipDownloader\Logs |
| 분할 | 날짜별, 파일당 10 MiB 초과 시 추가 분할 |
| 보관 | 최근 파일 14개; 14일 보장 아님 |
| 기본 수준 | Information 이상 |
| Phase 0 범위 | 시작·종료·초기화 실패·도구 탐색 결과 |

실행 파일 옆의 쓰기 권한을 가정하지 않는다. 후속 외부 프로세스 로그에서는 인증 정보·쿠키·서명된 스트림 URL을 기록하지 않는 원칙을 유지한다. 복잡한 제거 기능과 원시 프로세스 로그 처리는 이번 단계에 선행 구현하지 않는다.

로그 폴더 생성·로거 초기화의 예외와 Serilog SelfLog 진단을 확인한다. 상세 진단은 Debug 출력으로 보내고 문제가 감지되면 UI 스레드에서 "파일 로그 기록에 문제가 발생했습니다"를 실행 중 한 번 안내한다. 앱은 계속 실행한다. 실행 중 파일 기록 문제에도 같은 정책을 적용한다. SelfLog 진단을 다시 Serilog로 기록하지 않는다. 별도 복구·재시도 기능은 Phase 0에서 추가하지 않는다. 초기화 실패로 사용 가능한 로거가 없으면 기존 정책대로 Debug 진단으로 앱을 계속 실행한다.

공식 진단 근거: https://github.com/serilog/serilog/wiki/Debugging-and-Diagnostics

## 7. 확정한 도구 탐색과 진단 계약

대상은 yt-dlp.exe, ffmpeg.exe, ffprobe.exe다.

1. 명시한 사용자 실행 파일 경로
2. AppContext.BaseDirectory 아래 tools
3. PATH 디렉터리

사용자 상대 경로는 AppContext.BaseDirectory 기준이다. 명시한 사용자 경로에 파일이 없으면 누락, 경로 형식이 잘못되었거나 확인 불가하면 탐색 실패로 반환하며 다른 위치로 자동 대체하지 않는다. 사용자 설정이 없을 때만 tools, PATH를 순서대로 탐색한다.

Phase 0은 경로 탐색과 파일 존재 확인까지만 수행한다. 발견은 실행 가능·정상 버전의 보장이 아니다. 실행·버전 진단은 ProcessRunner를 구현하는 Phase 1 책임이다.

설정 모델과 탐색 서비스에 전달하는 연결까지만 준비한다. 저장·로드·설정 UI는 제외한다. 기본 앱은 사용자 경로가 없는 상태로 실행하고 명시 경로 동작은 테스트에서 확인한다.

```text
MainViewModel
→ IToolDiagnosticsService (Application)
→ ToolDiagnosticsService (Downloader.Infrastructure)
→ IToolLocator (Media.Infrastructure)
```

IToolLocator는 도구 하나를 탐색한다. 진단 서비스는 세 도구 결과를 모은다. Application의 반환 모델에는 도구 이름, 발견 경로, 발견 위치, 상태와 오류 사유를 담는다. Infrastructure 타입을 ViewModel로 노출하지 않는다.

결과 상태는 발견, 누락, 탐색 실패의 세 가지로 확정한다. 발견은 파일 존재 확인 성공, 누락은 유효한 경로를 확인했지만 파일 없음, 탐색 실패는 잘못된 경로 형식·접근 권한·입출력 오류 등으로 확인 불가를 의미한다. 설정 오류라는 별도 상태는 사용하지 않는다.

결과에는 사용자 지정 여부, 검사한 경로·출처, 구체적인 실패 사유를 보존하여 명시 경로의 누락과 기본 탐색의 누락을 구분해 안내한다. 탐색 실패는 가능한 로그에 상세 원인을 남기고 화면에는 이해 가능한 사유를 표시한다. 한 도구의 실패가 다른 도구의 진단이나 앱 실행을 막지 않는다.

기본 탐색에서는 tools 또는 PATH의 한 후보를 확인하지 못해도 오류를 기록하고 다음 후보를 계속 탐색한다. 뒤에서 파일을 찾으면 발견으로 반환하고 앞선 오류는 진단 정보로 보존한다. 끝까지 발견하지 못했으며 확인 불가 후보가 하나라도 있으면 탐색 실패로 반환한다. 모든 후보를 확인했으나 파일이 없으면 누락이다. 명시한 사용자 경로에는 이 계속 탐색 규칙을 적용하지 않으며 자동 대체하지 않는다. 메서드·모델의 상세 형태는 제14절에 기록했다.

## 8. 확정한 패키지와 관리 방식

각 csproj에 필요한 직접 의존성의 정확한 버전을 명시한다. 유동 버전과 Directory.Packages.props는 사용하지 않는다. 규모가 커져 반복 관리가 필요하면 중앙 관리를 재검토한다.

| 패키지 | 확정 버전 | 공식 배포 정보 |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2 |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.12 |
| Microsoft.Extensions.Logging | 10.0.12 | https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.12 |
| Serilog | 4.4.0 | https://www.nuget.org/packages/Serilog/4.4.0 |
| Serilog.Extensions.Logging | 10.0.0 | https://www.nuget.org/packages/Serilog.Extensions.Logging/10.0.0 |
| Serilog.Sinks.File | 7.0.0 | https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0 |
| xunit.v3.mtp-off | 4.0.1 | https://www.nuget.org/packages/xunit.v3.mtp-off/4.0.1 |
| xunit.runner.visualstudio | 4.0.0 | https://www.nuget.org/packages/xunit.runner.visualstudio/4.0.0 |
| Microsoft.NET.Test.Sdk | 18.10.1 | https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.10.1 |

xunit.v3.mtp-off의 4.0.1은 xUnit.net v3 제품군의 패키지 버전이다. 원래 확정한 xunit.v3 4.0.1의 MTP v2 기본 포함이 .NET 10의 VSTest 실행을 차단하여, 구현 검증에서 같은 버전의 MTP 제외 패키지로 변경했다(제17절). 테스트 실행은 VSTest의 dotnet test 및 Test Explorer를 기준으로 한다. 별도 mocking·assertion 패키지는 추가하지 않는다.

공식 배포 정보와 대상 프레임워크를 확인하여 설계 기준으로 채택했다. 실제 복원·빌드·테스트 성공을 검증한 목록은 아니다. 구현 검증에서 문제를 발견하면 원인을 기록하고 필요한 버전만 조정한다. 프로젝트별 직접 참조 배치와 Abstractions 추가 여부는 제13절에 기록했다.

## 9. 확정한 테스트 격리

ClipMaker.Media.Infrastructure.Tests 하나를 생성한다. 각 테스트는 자체 임시 폴더와 빈 실행 파일을 사용한다. Phase 0은 실행하지 않으므로 실제 바이너리는 필요 없다.

탐색기에 기준 디렉터리와 PATH 목록을 입력으로 전달하여 실제 설치 도구와 시스템 PATH에 의존하지 않는다. 테스트가 만든 파일만 종료 시 정리한다. 테스트만을 위한 파일 시스템 추상화는 추가하지 않는다.

필수 테스트는 우선순위, 명시 경로 누락·탐색 실패 시 자동 대체 금지, 상대 경로 기준, 발견·누락·탐색 실패 구분과 경로 출처·사유 보존이다. 기본 후보 오류 후 다음 후보 발견, 오류가 있고 끝까지 미발견, 오류 없이 미발견도 검증한다. 실제 권한 변경에 의존하는 비결정적 테스트는 피한다.

파일 확인 부분에는 교체 가능한 작은 검사 함수를 둔다. 실제 앱과 일반 테스트에서는 실제 파일 검사를 사용하고 오류 테스트에서는 특정 경로의 접근·입출력 오류를 반환하는 함수를 사용한다. 기본 디렉터리·PATH 입력 및 임시 폴더·빈 파일 방식은 유지한다. 파일 시스템 전체를 감싸는 별도 인터페이스는 추가하지 않는다.

## 10. 확정한 구현 순서와 완료 기준

1. 기존 앱 이동과 솔루션 경로 갱신. 이동 직후 빌드 확인.
2. 라이브러리 4개와 참조 관계 구성.
3. MVVM·DI·앱 시작과 종료 구성.
4. 로그와 도구 탐색 구현.
5. 최소 화면·비동기 재탐색 연결.
6. 테스트 구성과 최종 검증.

| 완료 조건 | 검증 계획 |
|---|---|
| 솔루션 빌드 | dotnet build ClipMaker.slnx |
| 도구 탐색 | dotnet test tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj |
| 계층 경계 | 프로젝트 참조 및 ViewModel의 Infrastructure 직접 사용 검토 |
| WPF 최소 화면 | 창 실행, 바인딩·명령·응답성 수동 확인 |
| 도구 누락 | 격리한 환경에서 앱 실행과 누락 표시 확인 |
| 로그 | 파일 생성과 시작·종료·탐색 기록 확인 |
| 실패 처리 | 잘못된 도구 설정, 로그 초기화 실패, 필수 초기화 실패 확인 |
| 정상 종료 | 탐색 중 창 종료 시 취소 요청·결과 반영 차단·프로세스 종료·자원 정리 확인 |

위 명령과 동작은 계획이며 수행 결과가 아니다. 필수 검증을 수행하지 못하면 미검증으로 남기고 구현 완료로 선언하지 않는다. 상세 절차는 제15절에 기록한다.

## 11. 미검증 사항과 인계

항목별 정책과 파일 배치를 합의했고 구체적인 연결·계약·검증 계획은 제13~15절에 정리했다. 2026-10-06 사용자의 "설계 확정할게" 지시로 전체 설계를 확정했다. 문서 링크와 트리는 실제 위치로 갱신했다. 이 확정은 구현 시작 또는 Phase 1 상세 설계 시작을 의미하지 않는다.

| 항목 | 현재 상태 | 처리 시점 |
|---|---|---|
| 설계 전체 승인 | 사용자 확정 완료 (2026-10-06) | 완료 |
| 패키지 복원·빌드·테스트·WPF 실행 | 실제 수행 결과는 제17절 | Phase 0 구현 |
| NuGet 접근과 대상 IDE 지원 | NuGet 복원·VSTest CLI 확인. Visual Studio Test Explorer 자체는 미실행 | IDE 사용 시 |
| .NET 파일 검사 API가 구분하는 오류 범위 | OS·드라이브가 보고하는 오류에 한정; 하드웨어 실패 원인까지 확정 불가 | Phase 0 구현·검증 |
| 로그 실패·종료 경합·UI 응답성 | 실제 수행 결과는 제17절 | Phase 0 구현 |
| 도구 실행 가능·버전·실제 미디어 처리 | Phase 0 범위 밖, 발견만으로 보장하지 않음 | Phase 1 |

현재 알려진 설계 차단 사항은 없다. 구현 검증 실패는 구현 완료 및 다음 Phase 시작을 차단한다. 패키지·IDE 문제 발생 시 원인과 최소 수정 사항을 기록한다.

Phase 1에는 구현·검증된 프로젝트 경계, Composition 방식, 도구 경로 계약, 로그 정책을 인계한다. Phase 1은 발견된 파일의 실제 실행·버전과 ProcessRunner를 검증한다. Phase 0에서 후속 시간 모델이나 다운로드 API를 확정하지 않는다.

## 12. 실제 수행 결과

문서·현재 소스·Git 상태 읽기와 SDK 목록 확인, 공식 패키지 문서 조회, 확정 사항의 설계 초안 기록을 수행했다. 구현·패키지 설치·빌드·테스트·앱 실행·외부 영상 다운로드는 수행하지 않았다.

세부 계약·파일 목록·패키지 배치·수동 검증 절차와 종료 경합 방지를 문서에 추가하고 체크리스트를 대조했다. PHASE_DESIGN과 PROJECT_STRUCTURE_DESIGN의 이동 전 경로 및 아키텍처의 상세 설계 링크를 수정했다. docs/ClipDownloader 전체의 로컬 Markdown 링크 24개를 검사하여 깨진 링크 0개를 확인했다. 체크리스트의 미체크 설계 항목은 0개이며 상세 설계의 코드 블록 구분자도 짝을 확인했다. 이 결과는 소스 빌드나 런타임 검증의 증거가 아니다. docs는 여전히 미추적 상태이고 임의 commit은 하지 않았다.

## 13. 합의한 파일 배치와 변경 목록

아래는 구현 시의 변경 계획이며 이 설계 작업에서는 소스 파일을 이동·생성하지 않는다. 기본 namespace와 AssemblyName은 ClipDownloader를 유지한다. MainWindow는 ClipDownloader.Views로 이동하고 XAML x:Class와 code-behind를 함께 변경한다. App은 ClipDownloader.App을 유지한다. MainWindow 생성자에서 InitializeComponent 후 주입받은 MainViewModel을 DataContext에 설정한다.

| 변경 | 파일 |
|---|---|
| 경로 갱신 | ClipMaker.slnx: 기존 앱 경로와 새 라이브러리 4개·테스트 프로젝트 등록 |
| 앱 이동·설정 수정 | ClipDownloader/ClipDownloader.csproj → apps/ClipDownloader/ClipDownloader.csproj |
| 앱 이동·시작 종료 수정 | 기존 App.xaml, App.xaml.cs → apps/ClipDownloader/; StartupUri 제거, OnMainWindowClose 지정 |
| 창 이동·namespace·바인딩 수정 | 기존 MainWindow.xaml, MainWindow.xaml.cs → apps/ClipDownloader/Views/ |
| 그대로 이동 | 기존 AssemblyInfo.cs → apps/ClipDownloader/AssemblyInfo.cs; ThemeInfo 보존 |
| 추가 | apps/ClipDownloader/ViewModels/MainViewModel.cs |
| 추가 | apps/ClipDownloader/ViewModels/ToolDiagnosticItem.cs: Application 결과를 화면 문구로 변환 |
| 추가 | apps/ClipDownloader/Composition/ServiceRegistration.cs, LoggingSetup.cs |
| 프로젝트 추가 | src 아래 확정한 라이브러리 4개의 이름과 동일한 csproj |
| 계약 추가 | src/ClipDownloader.Application/Contracts/IToolDiagnosticsService.cs |
| 결과 추가 | src/ClipDownloader.Application/Models/ToolDiagnostic.cs, ToolDiagnosticStatus.cs |
| 진단 서비스 추가 | src/ClipDownloader.Infrastructure/Diagnostics/ToolDiagnosticsService.cs |
| 탐색 추가 | src/ClipMaker.Media.Infrastructure/Tools/IToolLocator.cs, ToolLocator.cs |
| 설정·입력·결과 추가 | 같은 Tools 폴더: ToolPathSettings.cs, ToolSearchEnvironment.cs, ToolSearchRequest.cs, ToolLocationResult.cs, ToolLocationStatus.cs |
| 테스트 추가 | tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj, ToolLocatorTests.cs |

밀접한 결과 보조 enum·진단 항목은 해당 모델 파일에 함께 선언해도 된다. 형식적인 Class1, 빈 ViewModelBase, Commands·Dialogs·Resources 같은 미래 폴더는 생성하지 않는다. Media.Core는 csproj와 참조 경계만 준비한다. Downloader.Infrastructure의 Configuration 폴더와 설정 저장 클래스도 현재 필요가 없으므로 만들지 않는다.

프로젝트별 패키지 직접 참조는 다음과 같다. 버전은 제8절 확정 목록을 따른다.

- WPF 앱: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, Serilog, Serilog.Extensions.Logging, Serilog.Sinks.File.
- 라이브러리 4개: Phase 0에서는 외부 패키지 직접 참조 없음. 진단·탐색은 결과를 반환하고 WPF의 MainViewModel과 App이 ILogger<T>로 진단·수명 로그를 기록한다.
- 테스트: xunit.v3, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk. 테스트 어댑터에는 PrivateAssets=all을 적용한다. xUnit v3의 실행 프로젝트 요구에 맞춰 OutputType=Exe, IsTestProject=true, IsPackable=false를 구성하며 VSTest를 사용한다.

ILogger<T>만 필요한 후속 라이브러리에는 그 시점에 Logging.Abstractions를 검토한다. 지금은 사용하지 않는 Abstractions 패키지를 추가하지 않는다. 프로젝트 이동으로 영향을 받는 솔루션·XAML·프로젝트 참조 경로만 수정한다. 사용자 파일이나 bin/obj를 무분별하게 삭제하지 않는다.

## 14. 세부 계약과 앱 연결

### 계약과 모델

아래는 서명·모델 설계이며 실제 C# 구현 파일은 생성하지 않았다.

| 항목 | 설계 |
|---|---|
| IToolDiagnosticsService | Task<IReadOnlyList<ToolDiagnostic>> DiagnoseAsync(CancellationToken cancellationToken) |
| ToolDiagnostic | 도구 이름, 상태, 사용자 지정 여부, 선택된 절대 경로·출처, 검사 내역과 실패 사유 |
| ToolDiagnosticStatus | Found, Missing, SearchFailed |
| IToolLocator | ToolLocationResult Locate(ToolSearchRequest request, CancellationToken cancellationToken) |
| ToolPathSettings | 도구 실행 파일 이름 → 선택적인 사용자 지정 경로의 읽기 전용 매핑; null이면 미설정 |
| ToolSearchEnvironment | BaseDirectory의 절대 경로, PATH 디렉터리 목록의 snapshot |
| ToolSearchRequest | 실행 파일 이름과 선택적 명시 경로 |
| ToolLocationResult | 상태, 선택된 절대 경로·출처, 검사 내역·오류 사유 |
| ToolLocationStatus | Found, Missing, SearchFailed |
| 파일 검사 함수 | Func<string, FileAttributes>; 기본은 File.GetAttributes, 테스트는 특정 경로에서 예외를 발생시키는 함수 |

Media.Infrastructure는 yt-dlp 전용 enum이나 사이트 로직을 갖지 않는다. 진단 서비스가 세 실행 파일 이름을 지정하여 공통 탐색기를 호출하고 Application 결과로 변환한다. Application은 Media.Infrastructure의 결과 타입을 참조하지 않는다. 진단 모델의 출처는 UserSpecified, BundledTools, Path로 구분하고 상세 내역도 Application 자체 모델로 매핑한다.

ToolDiagnosticsService는 Task.Run에서 세 도구를 순차 탐색하고 결과를 반환한다. 외부 프로세스를 실행하지 않는다. 설정과 환경은 읽기 전용으로 구성한다. PATH는 Composition에서 프로세스 환경을 읽어 snapshot으로 전달하며 재탐색은 같은 설정을 사용한다. 실행 중 시스템 PATH 변경을 자동 수신하는 기능은 없다. 코드 수정으로 지정 경로를 주입하는 검증은 가능하지만 사용자 설정 저장·편집 기능은 없다.

MainViewModel은 ObservableObject와 AsyncRelayCommand를 사용한다. RefreshToolsCommand의 동시 실행은 허용하지 않고 IsBusy가 버튼·확인 중 표시를 제어한다. ToolDiagnostics는 완료 시 한 번에 교체하여 세 도구의 최신 결과를 표시한다. 최초 상태는 미확인/확인 중의 UI 상태이며 탐색 결과의 네 번째 enum 값으로 추가하지 않는다.

ServiceRegistration은 singleton ToolPathSettings·ToolSearchEnvironment 인스턴스를 등록하고 IToolLocator → ToolLocator, IToolDiagnosticsService → ToolDiagnosticsService, MainViewModel, MainWindow를 연결한다. 앱의 로그 provider는 App 소유 로거를 dispose: false로 연결한다. BuildServiceProvider의 ValidateOnBuild와 ValidateScopes로 구성 오류를 확인하고 메인 창을 명시적으로 resolve한다. 생성자 밖에서 컨테이너를 조회하는 코드는 Composition/App에만 둔다. MainWindow의 XAML x:Class와 code-behind namespace는 ClipDownloader.Views.MainWindow로 일치시킨다.

### 경로와 검사 세부 규칙

설정 null은 미설정이다. 명시된 빈 문자열·공백 문자열은 경로 형식 오류로 SearchFailed이며 자동 대체하지 않는다. 경로는 환경변수나 셸 명령으로 해석하지 않고 실행 파일 경로 자체로 취급한다. 상대 경로는 Path.GetFullPath(path, BaseDirectory) 방식으로 정규화하고 드라이브 상대 경로는 모호하므로 SearchFailed로 처리한다.

기본 후보는 BaseDirectory/tools/실행파일명, PATH 순서다. Windows PATH는 세미콜론으로 분리하고 항목 바깥 공백·한 쌍의 따옴표를 제거하며 빈 항목은 제외한다. 상대 PATH 항목도 앱 기준으로 정규화한다. 현재 작업 디렉터리나 PATHEXT로 추가 탐색하지 않는다. 설정 순서를 보존하며 필요한 경우 정규화된 같은 후보의 중복 확인을 생략할 수 있다.

File.Exists는 접근·검사 오류에서도 false를 반환하므로 결과 세 가지를 구분하는 기본 검사로 사용하지 않는다. File.GetAttributes로 확인하며 디렉터리는 실행 파일로 인정하지 않는다. FileNotFoundException과 DirectoryNotFoundException은 해당 후보 미발견, 경로 형식·권한·기타 입출력 오류는 확인 실패로 기록한다. OS가 미존재로 보고한 경로는 미발견으로 처리하며 정확한 하드웨어 고장 원인을 추정하지 않는다. 예상 가능한 경로·파일 시스템 오류만 결과로 변환하고 프로그래밍 오류를 누락으로 숨기지 않는다.

명시한 경로가 디렉터리이면 유효한 실행 파일 지정이 아니므로 SearchFailed로 처리한다. 기본 탐색의 실행파일명 위치가 디렉터리이면 그 후보는 파일 미발견이며 다음 후보를 확인한다. 발견 후 변경·삭제될 가능성이 있으므로 Phase 1 실행 시에도 오류를 처리한다.

API 근거: [File.Exists](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.exists?view=net-10.0), [File.GetAttributes](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.getattributes?view=net-10.0).

### 종료와 비동기 경합

App은 종료를 시작할 때 UI 스레드에서 MainViewModel.Stop을 호출한다. Stop은 닫힘 상태를 설정하고 새 명령을 막은 뒤 현재 탐색의 CancellationTokenSource를 취소한다. 명령의 결과 적용과 ILogger 사용도 UI 스레드에서 이루어지고 await 직후 닫힘 상태를 먼저 검사한다. 닫힘 상태이면 화면·DI 서비스·로거를 사용하지 않고 반환한다. 취소는 SearchFailed로 변환하지 않는다.

백그라운드 탐색기와 진단 서비스는 ILogger, WPF 타입, disposable 자원을 보유하지 않는다. 따라서 DI·로거가 정리된 후 지연된 파일 확인이 끝나더라도 결과를 계산하는 데 닫힌 자원이 필요 없다. 파일 확인을 강제로 중단하거나 무기한 Join/Wait하지 않는다. 취소 토큰은 시작 시 캡처하고 탐색이 완료될 때 해당 요청의 CancellationTokenSource를 finally에서 정리한다. 완료가 종료 뒤에 도착해도 예외가 관찰되도록 명령 내부에서 취소·예외를 처리하며, 종료 후의 예외 진단은 Debug만 사용한다.

MainWindow code-behind는 DataContext 연결 등 WPF 작업만 담당한다. 앱 시작 후 최초 탐색과 창 Closing의 Stop 연결은 Composition/App에서 구성한다. App의 OnExit는 자원 정리의 단일 경로로 사용하고 시작 실패에도 한 번만 호출되게 한다. 필수 초기화 실패의 종료 코드는 0이 아닌 값, 정상 종료는 0이다.

### 로그 내부 진단 연결

LoggingSetup은 SelfLog를 로거 생성 전에 연결한다. 콜백은 Debug 기록과 원자적인 문제 감지 플래그만 수행하며 Serilog·WPF를 직접 호출하지 않는다. App은 UI가 준비된 뒤 플래그를 확인하고, 실행 중의 새 문제는 Dispatcher로 알림을 예약하여 한 번만 표시한다. Dispatcher 종료 후에는 안내를 예약하지 않는다. 초기화 실패 시 잔여 로거 자원을 정리하고 DI에는 로그가 없는 provider 구성을 사용하여 ILogger 주입은 유지한다. Debug 출력은 진단 데이터의 영구 보존을 보장하지 않는다.

로거의 직접 참조를 App이 유지하여 DI 정리 이후에도 종료 로그를 기록한다. SelfLog 등록도 종료 시 해제하며 콜백이나 탐색 완료가 폐기된 로거로 다시 진입하지 않도록 한다. 로그 파일은 clipdownloader-날짜.log 계열, 텍스트·UTF-8, 타임스탬프·수준·카테고리·메시지·예외를 기록한다. Phase 0의 소량 로그에는 별도 비동기 sink 패키지를 추가하지 않는다. 여러 프로세스의 동일 로그 사용에는 File sink의 shared 설정을 사용하며 보관 한도는 순간적인 동시 기록 상황의 엄밀한 저장 용량 상한이 아니다.

## 15. 상세 검증 절차와 기록 형식

### 구현 시작과 자동 검증

1. Git 상태·기준 commit과 사용자 변경 확인, dotnet --info 및 SDK 확인.
2. 앱 이동 직후 dotnet build ClipMaker.slnx 실행; 실패 시 이동·참조·XAML 경로 확인.
3. 구현 완료 시 dotnet restore ClipMaker.slnx, dotnet build ClipMaker.slnx --no-restore, dotnet test tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj --no-build 실행.
4. 솔루션 포함 항목과 TFM·프로젝트 참조를 검토하고 ViewModels에서 Infrastructure namespace 사용이 없는지 확인.

테스트는 사용자 경로 우선, tools 우선, PATH 순서, 명시 경로 미존재·검사 실패 시 fallback 금지, 상대 경로 기준, 빈 PATH, 디렉터리 오지정, 세 결과 상태, 오류 후보 후 발견·전체 미발견, 진단 내역 보존을 검증한다. 취소 토큰은 후보 사이 취소를 확인하고 취소를 실패 결과로 변환하지 않는지 검증한다. 검사 오류는 교체 함수를 사용한다. 일반 파일 테스트는 실제 임시 파일·디렉터리를 사용하고 작업 디렉터리나 시스템 PATH를 변경하지 않는다.

### 수동 검증

| 사례 | 재현과 확인 |
|---|---|
| 최초 실행 | dotnet run --project apps/ClipDownloader/ClipDownloader.csproj --no-build. 창 표시 후 확인 중 → 결과, 다시 확인 버튼의 비활성화·복귀 확인 |
| 상태 갱신 | 격리한 앱 출력의 tools에 빈 파일을 추가·제거하고 재탐색. 결과가 바뀌고 실제 실행은 하지 않는지 확인 |
| 전체 도구 누락 | 임시 배포 출력 복사본의 tools를 비우고 자식 앱 프로세스에만 도구 없는 PATH 전달. 창 실행과 세 도구 Missing 확인; 시스템 PATH 변경 금지 |
| 지정 경로 누락·오류 | 임시 검증용 Composition 입력에 미존재 경로·잘못된 경로를 전달. Missing/SearchFailed와 지정 경로 사유, 다른 위치 자동 대체 금지 확인 |
| 응답성과 종료 | 임시 검사 함수로 후보 검사를 지연시키고 창 이동·버튼 비활성화 확인. 탐색 중 닫기, 종료 지연·종료 후 결과 및 로그 반영 없음 확인 |
| 기본 로그 | 사용자 로그 폴더에서 시작·도구 결과·종료 기록 및 카테고리 확인. 구현한 날짜·크기·보관 설정도 검토 |
| 분할·보관 | 격리한 임시 로그 폴더에 검증 코드로 10 MiB 초과 기록과 14개 초과 파일을 생성해 분할·보관 확인. 실제 사용자 로그 폴더의 파일은 삭제하지 않음 |
| 로그 초기화 실패 | 임시 로그 경로의 상위 위치를 파일로 만들어 폴더 생성 실패를 재현. 앱 유지와 한 번 안내·Debug 진단 확인 |
| SelfLog 실패 | 검증 코드에서 던지는 sink로 내부 진단 발생. 한 번 안내·재귀 없음·앱 유지 확인. 검증 sink는 제품 등록에 남기지 않음 |
| 필수 초기화 실패 | 검증 Composition에서 필수 등록 누락 등으로 창 생성 실패 재현. 안내·가능한 로그·실패 종료·중복 정리 없음 확인 |

검증용 입력·함수·코드는 로컬 임시 검증에 한정하며 사용자용 설정 UI, 숨은 제품 플래그, 추가 운영 기능으로 남기지 않는다. 검증 후 등록을 복원하고 최종 빌드·관련 테스트를 다시 확인한다. 재현 전에 대상 절대 경로가 이번 검증용 임시 폴더인지 확인하고 해당 폴더만 정리한다.

각 실제 결과는 날짜, 기준 commit·미커밋 파일, SDK·패키지 버전, 실행 명령·재현 입력, 예상/실제 결과, 종료 코드·로그 증거, 실패·미검증 사유와 처리 시점을 기록한다. 도구 실행·다운로드는 Phase 0 검증에 요구하지 않는다.

## 16. 설계 확정 기록

- 확정일: 2026-10-06 (Asia/Seoul).
- 근거: 이 Phase 0 설계 대화에서 항목별 결정·파일 배치를 합의하고 사용자가 전체 설계를 명시적으로 확정했다.
- 확정 범위: 기본 실행 기반, 프로젝트·파일 배치·참조, MVVM·DI·앱 수명, 로깅, 도구 탐색·진단, 패키지 버전, 테스트 및 검증 계획.
- 설계 차단 사항: 현재 알려진 사항 없음. 실제 환경의 복원·빌드·테스트·실행은 제11절의 미검증 사항으로 유지한다.
- 다음 단계: 별도 구현 요청 후 확정 설계에 따라 Phase 0을 구현하고 필수 검증 결과를 기록한다. 구현·검증 완료 전 Phase 1 상세 설계는 시작하지 않는다.
- 구현 context 시작 자료: [Phase 0 구현 인계](PHASE_0_IMPLEMENTATION_HANDOFF.md). 사용자는 구현을 별도 context에서 진행하기로 했다. 이 설계 context에서는 인계 문서까지만 작성했다.

## 17. 구현 context의 실제 수행 결과

### 기준과 최종 소스 상태

- 수행일: 2026-10-06 (Asia/Seoul). 같은 날 UI 검증 중 사용자의 Escape 입력으로 중단한 뒤, 사용자의 재개 요청에 따라 남은 검증을 수행했다.
- 기준 HEAD: `fc96883b18c1d6856c92ad6b029d45e095e41e69`. 시작 시 `git status --short`는 `?? docs/`였으며 기존 WPF 파일의 사용자 수정은 없었다.
- SDK 10.0.302, MSBuild 18.6.11, Windows x64, .NET/WindowsDesktop runtime 10.0.10을 실제 확인했다.
- 최종 변경은 미커밋 상태다. Git에는 기존 `ClipDownloader/`의 추적 파일 6개 삭제, `ClipMaker.slnx` 수정, `apps/`, `src/`, `tests/`, `docs/` 미추적으로 표시된다. 기존 앱 삭제 표시는 `apps/ClipDownloader/`로의 이동이며 파일을 버린 것이 아니다. 기존 bin/obj 및 프로젝트 사용자 파일도 이동 때 보존했다.
- 별도 commit·push, Phase 1 설계·구현, 도구 실행·다운로드는 수행하지 않았다. 사용자 docs 및 사용자 로그를 삭제하지 않았다.

### 구현 내용과 설계 조정

- 앱을 `apps/ClipDownloader`로 이동하고 라이브러리 4개와 테스트 1개를 기존 slnx에 등록했다. TFM과 직접 프로젝트 참조는 제3절 표와 일치한다. Media.Core에는 빈 클래스 없이 csproj만 둔다.
- Application 계약은 `Contracts/IToolDiagnosticsService.cs`, 결과는 `Models/ToolDiagnostic.cs`, 진단 구현은 Downloader.Infrastructure의 `Diagnostics/ToolDiagnosticsService.cs`에 두었다. 밀접한 enum·검사 내역 모델은 결과 파일에 함께 선언했다.
- MainWindow의 XAML/code-behind를 Views로 옮겼다. 생성자 주입, singleton 등록, ValidateOnBuild/ValidateScopes, App의 시작·종료 소유권을 구현했다. ViewModel에는 Infrastructure 참조가 없다.
- ObservableObject·AsyncRelayCommand로 상태 표시와 재탐색을 연결했다. `ToolDiagnosticItem`은 결과의 화면 문구 변환만 담당한다. 창 표시 후 ContextIdle에서 최초 진단을 실행한다.
- 탐색은 명시 경로 → tools → PATH이며 명시 경로에는 자동 대체가 없다. 상대 경로, PATH 공백·따옴표, 디렉터리 오지정, 기본 후보 오류 후 탐색 계속, 결과·오류 내역 보존, 후보 사이 취소를 구현했다. 실제 파일 검사에는 File.GetAttributes를 사용한다.
- SelfLog 콜백은 Debug 기록과 원자적인 감지 플래그만 변경한다. App의 UI DispatcherTimer가 250ms 간격으로 플래그를 확인해 실행 중 한 번 안내한다. 종료 시작 시 타이머와 새 탐색을 막고 취소한 후 DI → 종료 로그 → 로거 → SelfLog 순으로 정리한다.
- 수동 검증에서 명시 경로 오류 문구가 지나치게 일반적이어서 사용자 지정/기본 탐색을 구분하고 안전한 원인 요약을 표시하도록 보완했다. 원시 예외 정보는 결과 모델과 로그에 보존한다.
- 경고창을 소유자 없이 표시했을 때 UI 도구가 메인 창 밖의 모달을 조작하지 못한 사례가 있었다. 경고 MessageBox에 MainWindow 소유자를 연결해 모달 포커스를 보완하고 키보드 확인·재탐색·정상 종료를 재검증했다. 실패했던 임시 실행은 강제 정리했으며 정상 종료 성공 증거로 사용하지 않았다.

**패키지 수정 근거:** 최초 복원과 컴파일은 `xunit.v3 4.0.1`로 성공했으나 dotnet test는 종료 코드 1로 실패했다. 오류는 “Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later”였다. 복원된 nuspec과 공식 [xUnit 4.0 릴리스 설명](https://xunit.net/releases/v3/4.0.0), [MTP 제외 패키지 메타데이터](https://www.nuget.org/packages/xunit.v3.mtp-off/4.0.1)를 대조했다. xunit.v3 4.0.1은 MTP v2를 포함하므로 확정한 VSTest를 유지하려면 `xunit.v3.mtp-off 4.0.1`이 필요하다. 제품군·버전·테스트 계약·나머지 패키지와 VSTest 어댑터는 유지했다. 새로운 global.json이나 MTP 실행 방식을 도입하지 않은 최소 수정이다.

### 자동 검증

| 명령·검증 | 실제 결과 |
|---|---|
| dotnet --info | 권한 제한 환경에서는 Windows 서비스 관리자 접근 실패. 승인된 환경에서 재실행하여 SDK/runtime 확인, 종료 코드 0 |
| 앱 이동 직후 dotnet build ClipMaker.slnx | 최초 샌드박스 실행은 사용자 .NET 초기화 파일 쓰기 제한으로 실패. 승인된 환경에서 재실행하여 성공, 경고 0·오류 0·종료 코드 0 |
| 최종 dotnet restore ClipMaker.slnx | 모든 프로젝트 복원 완료, 종료 코드 0 |
| 최종 dotnet build ClipMaker.slnx --no-restore | 성공, 경고 0·오류 0·종료 코드 0 |
| 최종 dotnet test tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj --no-build | VSTest 실행, 22 통과·0 실패·0 건너뜀, 종료 코드 0 |
| git diff --check | 최종 검사 출력 없음, 종료 코드 0 |
| TFM·프로젝트 참조·ViewModel 경계 검토 | 제3절과 일치. ViewModels에 Infrastructure 검색 결과 없음(검색 종료 코드 1은 정상적인 무일치) |

구현 중 WPF의 System.IO using 누락으로 컴파일 오류 3개가 발생하여 명시 using을 추가했다. xUnit 취소 토큰 분석 경고 14개는 일반 테스트에서 TestContext.Current.CancellationToken을 사용하여 해소했다. 최초 실행된 22개 중 7개 테스트는 기대 경로가 혼합 구분자 문자열인 탓에 실패했다. 테스트의 임시 파일 생성 helper에서 기대 절대 경로를 Path.GetFullPath로 정규화한 후 모두 통과했다. 실패 결과를 완료 증거로 사용하지 않았다.

22개 테스트는 사용자 경로·tools·PATH 우선순위, 명시 경로 누락·오류의 자동 대체 금지, 상대 경로 기준, 빈/따옴표 PATH, 디렉터리 오지정, 중복 후보, 오류 후 발견/전체 미발견, 사유·출처 보존, snapshot 불변성, 취소 및 프로그래밍 오류 전파를 검증한다. 실제 설치 도구·시스템 PATH·권한 변경에 의존하지 않는다.

### 실제 실행·실패·수명 검증

검증 복사본과 하네스는 저장소 안의 `.phase0-validation`에만 만들었고 솔루션에 포함하지 않았다. 복사본은 제품의 WPF·Composition 코드를 사용하면서 탐색 입력·검사 함수·로그 폴더·필수 등록·sink만 임시 교체했다. 하네스는 최종 제품 프로젝트를 참조하여 실제 ViewModel·MainWindow·진단 서비스 및 LoggingSetup을 사용했다.

| 사례와 재현 입력 | 관찰과 증거 |
|---|---|
| 제품 앱 최초/최종 실행: dotnet run --project apps/ClipDownloader/ClipDownloader.csproj --no-build | 실제 창과 바인딩, 자동 진단, 다시 확인 명령 확인. yt-dlp Missing, FFmpeg/ffprobe PATH Found. 마지막 실행도 정상 종료 코드 0 |
| 전체 누락: 격리된 기준 폴더, 빈 PATH snapshot | 세 도구 Missing 상태로 앱 실행 유지. 임시 tools의 빈 yt-dlp.exe 추가 → 재탐색 Found, 제거 → 재탐색 Missing. 실행 파일을 실행하지 않음 |
| 명시 경로: yt-dlp=absent.exe, FFmpeg=C:ambiguous.exe, ffprobe=빈 문자열 | 실제 화면에서 Missing/SearchFailed/SearchFailed, 앱 유지. 지정 상대 경로의 절대 검사 경로와 사유가 로그에 보존됨. 자동 대체 금지는 단위 테스트와 함께 확인 |
| 기본 후보 오류 및 진단 매핑: tools 검사에 UnauthorizedAccessException, 뒤 PATH 후보는 파일 속성 성공, 한 도구는 명시 빈 경로 | 하네스에서 세 도구 결과, 한 도구의 실패 격리, 후속 PATH Found와 앞선 오류·사유·출처 보존 확인 |
| 파일 검사 60초 지연 | 실제 창의 확인 중 표시와 버튼 disabled 상태 관찰. 탐색 중 창 이동 가능. 닫기 입력 후 창이 사라지는 데 약 117ms(도구 측정), 프로세스 종료 코드 0. 60초 검사 완료를 기다리지 않음 |
| 종료 뒤 늦은 완료·예외: TaskCompletionSource로 지연 결과/IOException 반환 | 최종 제품 ViewModel·WPF 바인딩 하네스에서 중복 요청 1회만 수행, busy 버튼 비활성화, Stop 취소 요청과 새 명령 차단 확인. 창을 닫고 로거를 사용 불가 상태로 둔 뒤 결과/예외를 전달해도 기존 화면 목록과 logger 호출 수 0 유지, 작업 예외 관찰 완료 |
| 로그 초기화 실패: 임시 로그 폴더의 상위 경로를 파일로 생성 | 실제 IOException 재현. “파일 로그 기록에 문제가 발생했습니다” 안내 확인. 기록된 안내 1회·정리 1회, 이후 재탐색 가능, 정상 종료 코드 0 |
| SelfLog: 매 기록에 IOException을 던지는 임시 sink | 실제 SelfLog 오류 15회, 안내 1회·정리 1회. 안내 확인 후 재탐색·앱 유지·정상 종료 코드 0. 진단 콜백에 Serilog 재진입 없음 |
| 필수 초기화 실패: MainWindow DI 등록 제거 | 실제 오류 안내. 로그에 InvalidOperationException과 누락된 서비스 타입, 초기화 실패, 종료 코드 1 기록. 안내 확인 후 실행 종료 코드 1, 정리 1회 |
| 기본 사용자 로그 | 실제 %LOCALAPPDATA%/ClipMaker/ClipDownloader/Logs/clipdownloader-20261006.log에 시작·초기화·도구와 후보 결과·출처·사유·종료 및 SourceContext 기록 확인. 최종 종료 기록은 23:31:45.384 +09:00, 종료 코드 0 |
| 분할·보관: 실제 LoggingSetup.Configure를 사용해 격리 로그 폴더에 1MiB payload 180회 기록 | 날짜 파일과 크기 분할을 확인. 최근 14개만 보관, _004~_017 파일, 크기 10,486,390~10,486,400 bytes. Information 미만의 Debug 메시지는 기록하지 않음 |

지연 앱 로그는 시작(23:25:34.297)·초기화(23:25:35.463)·종료(23:26:04.928, 코드 0)만 남고 지연된 도구 결과 기록은 없었다. 실제 프로세스 종료와 별도 지연 완료 하네스를 함께 사용하여 즉시 파일 호출 취소 보장이 아닌 결과 반영 차단·종료 책임을 확인했다.

날짜별 rollover 설정, UTF-8, shared, 10MiB 한도, 14파일 보관, Information 수준 및 App 소유/dispose:false 연결을 코드와 실제 출력으로 대조했다. 파일 sink의 크기 제한은 기록 경계에서 분할하므로 한 레코드 크기만큼 한도를 넘을 수 있다. 위 관찰을 엄밀한 저장 용량 상한으로 해석하지 않는다.

### 정리·남은 제한·인계

- 제품 등록에는 검증용 sink·지연 함수·환경 플래그·테스트 입력이 없다. 검증 root의 절대 경로가 저장소의 `.phase0-validation`이고 링크가 아닌 것을 확인한 뒤 그 폴더만 정리했다. 최종 Test-Path 결과 False, Git 상태에도 검증 폴더가 없다.
- 검증 후 최종 제품 파일로 빌드·22개 테스트·WPF 실행·종료를 확인했다. 문서의 설계 context 수행 기록과 사용자 기준 문서는 보존했다.
- 필수 Phase 0 완료 조건에 남은 차단 사항은 없다. Visual Studio Test Explorer 자체, 다음 날짜를 기다리는 날짜 경계 전환, 영구 Debug 출력 수집은 별도로 실행하지 않았다. VSTest CLI·날짜별 설정·실제 날짜 파일·SelfLog 진단과 안내 정책을 검증했으며 이 항목들은 Phase 0 필수 조건을 대신 생략한 것이 아니다.
- 파일 발견은 실행 가능·버전·미디어 처리 성공의 보장이 아니다. 현재 yt-dlp 누락은 기본 실행을 막지 않으며, 실제 도구 설치·버전·실행 검증은 Phase 1에서 다룬다.
- 다음 Phase는 이 문서와 미커밋된 현재 소스를 읽고 시작한다. Application의 진단 계약, 읽기 전용 경로 입력, Media.Infrastructure 탐색 결과·검사 내역, App의 로그 소유권과 종료 순서를 사용한다. Phase 1 상세 설계는 이번 작업에서 시작하지 않았다.
