# ClipDownloader Phase 0 완료 인계 요약

작성일: 2026-10-07 (Asia/Seoul). 종합 계획 context의 Phase 1 준비 자료 통합용이다. 실제 완료 결과와 후속 제약만 요약하며 Phase 1 상세 설계·새 기능을 확정하지 않는다.

## 1. 완료 기준점과 검증 근거

| 항목 | 확인된 내용 |
|---|---|
| Base | `fc96883b18c1d6856c92ad6b029d45e095e41e69` |
| Completion | `0759dc52475538df848f765ce30c005cf755107f` |
| 완료 태그 | `phase-0-complete` — 위 Completion 커밋을 가리킴 |
| Branch | `main` |
| 상태 | Phase 0 구현·필수 검증 완료 |
| 검증 수행일 / 완료 커밋일 | 2026-10-06 / 2026-10-07 |
| 실행 환경 | Windows x64, SDK 10.0.302, MSBuild 18.6.11, .NET/WindowsDesktop runtime 10.0.10 |

검증 근거는 완료 커밋에 포함된 [상세 설계 제17절](PHASE_0_DESIGN.md#17-구현-context의-실제-수행-결과)이다. 복원·빌드·VSTest는 종료 코드 0, 최종 빌드 경고·오류 0개, 테스트 22개 통과·실패/건너뜀 0개였다. 실제 WPF 최초 진단·재탐색·상태 갱신·전체 도구 누락 시 실행 유지도 확인했다.

필수 실패·수명 검증은 로그 초기화 실패와 SelfLog 오류의 한 번 안내·앱 유지, 필수 DI 초기화 실패의 안내·종료 코드 1, 60초 파일 검사 지연 중 UI 응답성과 정상 종료 코드 0을 포함한다. 별도 하네스에서 종료 후 늦은 결과·예외의 화면 반영/로거 사용 차단을 확인했다. 실제 로그 설정으로 10MiB 크기 분할·최근 파일 14개 보관도 검증했다.

이 요약 작성에서는 Git 기준점과 현재 소스·기존 검증 기록을 대조했으며 빌드·실행을 다시 수행하지 않았다. 상세 설계 제17절의 “미커밋 상태”는 검증 종료 당시의 기록이다. 이후 사용자가 위 완료 커밋과 태그를 만들었다. [Phase Baseline](../PHASE_BASELINE.md)은 그 기준점을 기록하며, 요약 작성 시 Baseline 문서는 아직 미추적 상태다.

## 2. 실제 프로젝트 경계와 재사용할 계약

단일 `ClipMaker.slnx`에 실행 앱 1개, 라이브러리 4개, 테스트 1개가 있다.

| 프로젝트 / 위치 | 실제 책임 | 직접 프로젝트 참조 |
|---|---|---|
| ClipDownloader / apps | net10.0-windows WPF, View/ViewModel, Composition, 앱·로그 수명 | Application, Media.Core, 두 Infrastructure |
| ClipDownloader.Application / src | net10.0 도구 진단 계약·반환 모델 | Media.Core |
| ClipDownloader.Infrastructure / src | net10.0 세 도구의 진단 조합·Application 모델 매핑 | Application, Media.Core, Media.Infrastructure |
| ClipMaker.Media.Core / src | net10.0 프로젝트 경계만 존재. 모델·계약 구현 없음 | 없음 |
| ClipMaker.Media.Infrastructure / src | net10.0 공통 실행 파일 경로 탐색 | Media.Core |
| ClipMaker.Media.Infrastructure.Tests / tests | net10.0 격리된 도구 탐색 테스트 | Media.Infrastructure |

WPF의 Infrastructure 사용은 Composition에 한정되며 ViewModel은 Application 계약을 사용한다. CLI·ClipEditor·ProcessRunner·미디어/시간 모델은 존재하지 않는다.

재사용할 실제 계약과 입력은 다음과 같다.

- [IToolDiagnosticsService](../../../src/ClipDownloader.Application/Contracts/IToolDiagnosticsService.cs): `Task<IReadOnlyList<ToolDiagnostic>> DiagnoseAsync(CancellationToken)`. 구현은 세 실행 파일을 Task.Run에서 순차 탐색한다.
- [ToolDiagnostic](../../../src/ClipDownloader.Application/Models/ToolDiagnostic.cs): 도구 이름, Found/Missing/SearchFailed, 사용자 지정 여부, 선택 절대 경로·출처, 검사 내역·실패 사유. Application 자체 모델이며 Infrastructure 타입을 UI로 노출하지 않는다.
- [IToolLocator](../../../src/ClipMaker.Media.Infrastructure/Tools/IToolLocator.cs): `ToolLocationResult Locate(ToolSearchRequest, CancellationToken)`. 실행 파일 이름을 받는 동기 공통 탐색 계약으로 특정 사이트나 yt-dlp 전용 enum이 없다.
- [ToolSearchRequest](../../../src/ClipMaker.Media.Infrastructure/Tools/ToolSearchRequest.cs) / [ToolLocationResult](../../../src/ClipMaker.Media.Infrastructure/Tools/ToolLocationResult.cs): 실행 파일 이름·선택적 명시 경로를 입력받고 세 상태, 사용자 지정 여부, 선택 경로·출처와 모든 검사 내역을 반환한다. 출처는 UserSpecified/BundledTools/Path다.
- [ToolPathSettings](../../../src/ClipMaker.Media.Infrastructure/Tools/ToolPathSettings.cs) / [ToolSearchEnvironment](../../../src/ClipMaker.Media.Infrastructure/Tools/ToolSearchEnvironment.cs): 실행 파일 이름별 읽기 전용 경로 매핑, 절대 기준 디렉터리와 PATH snapshot. 기본 앱은 사용자 지정 경로가 없으며 PATH는 시작 시 한 번 읽는다.

보존할 탐색 규칙은 명시 경로 → 앱 기준 tools → PATH다. 명시 경로 누락·오류에는 자동 대체가 없고, 기본 후보 오류는 내역을 남기고 다음 후보를 확인한다. 뒤에서 발견하면 Found와 앞선 오류를 보존하며, 미발견 시 오류가 있으면 SearchFailed, 없으면 Missing이다. 상대 경로는 AppContext.BaseDirectory 기준이고 모호한 드라이브/루트 상대 경로는 거부한다. File.GetAttributes로 파일 존재만 확인하고 취소를 실패 상태로 변환하지 않는다.

앱의 singleton DI·MVVM 연결과 로그 소유권도 완료된 기반이다. Serilog 로거는 App 소유, DI 연결은 dispose:false이며 종료 순서는 새 탐색 차단·취소 → DI 정리 → 종료 로그 → 로거 마무리·SelfLog 해제다. 탐색·진단 서비스는 UI·로거·disposable 자원을 보유하지 않는다.

## 3. 설계 대비 변경과 이유

| 변경 | 이유와 영향 |
|---|---|
| xunit.v3 4.0.1 → xunit.v3.mtp-off 4.0.1 | 원래 패키지가 MTP v2를 포함해 .NET 10의 VSTest 실행을 차단했다. 확정한 VSTest와 제품군·버전을 유지하는 최소 패키지 수정이며, 나머지 패키지는 확정 버전을 유지했다. |
| ToolDiagnosticItem 추가 및 오류 문구 보완 | Application 결과를 화면 문구로 변환하고 사용자 지정/기본 탐색과 안전한 원인 요약을 구분한다. 원시 예외 정보는 결과·로그에 보존한다. |
| 로그 경고 MessageBox에 MainWindow 소유자 연결 | 소유자 없는 모달을 검증 도구가 조작하지 못한 사례를 계기로 모달 포커스를 보완했다. 수정 후 안내 확인·재탐색·정상 종료를 재검증했다. |
| SelfLog 감지 플래그를 UI DispatcherTimer로 확인 | 콜백의 Debug 기록·원자적 플래그와 UI 안내를 분리한 연결 방식이다. 250ms 간격, 실행 중 한 번 안내, 종료 시 타이머 중지 정책으로 구현했다. |

프로젝트 참조 방향, 세 결과 상태, 명시 경로의 자동 대체 금지, 파일 존재 확인 범위와 앱·로그 종료 책임은 유지했다. 구현 중 컴파일·테스트 helper 오류는 수정 후 재검증했으며 설계 변경으로 취급하지 않는다.

## 4. 남은 제한·미검증 및 Phase 1 확인 항목

Phase 0 필수 완료 조건에 남은 차단 사항은 없다. 다음 사실을 Phase 1 준비 자료에 그대로 전달한다.

- **Found는 파일 존재만 뜻한다.** 실제 실행 가능 여부·도구 버전·기능 지원·정상 미디어 처리는 검증하지 않았다. 도구 실행 전에 발견 이후 파일 변경/삭제 및 실행 실패를 처리해야 한다.
- 검증 당시 실제 환경은 yt-dlp Missing, FFmpeg/ffprobe PATH Found였다. FFmpeg/ffprobe도 실행·버전을 확인하지 않았다. Phase 1 시작 환경에서 도구 설치 상태·실행·버전을 다시 확인해야 한다.
- ProcessRunner, stdout/stderr 처리, 프로세스·자식 프로세스 취소, yt-dlp JSON, FFmpeg 처리, 시간/FPS 모델, CLI와 실제 CHZZK/YouTube 클립 검증은 Phase 0에 구현·검증되지 않았다. Phase 1은 이를 이미 제공된 기능으로 가정할 수 없다.
- 탐색 취소는 후보 사이에서 확인한다. 파일 시스템 호출 자체의 즉시 취소는 보장하지 않는다. Phase 0 종료 검증은 파일 탐색에 대한 것이며 외부 프로세스 수명·취소의 검증 근거가 아니다.
- 설정은 읽기 전용 입력 전달만 존재하고 저장·로드·편집 UI는 없다. 재탐색도 시작 시의 PATH snapshot을 사용하며 실행 중 환경 변경을 자동 반영하지 않는다.
- 일반 net10.0 라이브러리 TFM은 다른 OS 동작 보장이 아니다. 실제 지원·검증 대상은 Windows다.
- Visual Studio Test Explorer, 실제 날짜 경계에서의 로그 전환, Debug 출력의 영구 수집은 별도로 실행하지 않았다. VSTest CLI, 날짜별 설정·실제 날짜 파일, SelfLog 진단·안내 동작은 검증했다.
- 로그 보관은 14일이 아니라 최근 파일 14개다. 크기는 기록 경계에서 분할하므로 한 레코드만큼 10MiB를 초과할 수 있으며 shared sink의 보관 정책을 엄밀한 저장 용량 상한으로 해석하지 않는다.
- 실패·지연 검증용 복사본·하네스는 정리됐고 완료 소스에 숨은 검증 플래그·sink·지연 함수가 없다. 상세 재현 입력과 관찰은 제17절에 남아 있으나 임시 하네스 파일 자체는 재사용할 수 없다.

종합 계획 context는 완료 커밋의 실제 계약·제약과 [Phase 운영 규칙](../PHASE_DESIGN.md)을 기준으로 Phase 1 준비 자료를 통합한다. 이 요약으로 Phase 1의 API·구현 방식이나 추가 범위를 확정하지 않는다.
