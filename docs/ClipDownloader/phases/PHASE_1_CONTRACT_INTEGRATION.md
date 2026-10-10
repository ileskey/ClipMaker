# Phase 1 하위 계약 통합·파일 변경 계획

작성일: 2026-10-10 (Asia/Seoul).

상태: **구현 전 계약 구체화**. [상세 설계](PHASE_1_DESIGN.md)의 채택 정책을 유지한 구현 방안이다. 아래 시그니처는 아직 제품 코드가 아니며 컴파일 검증을 수행하지 않았다. 실험 의존 타입·전략은 별도 보류한다. 일반 정책을 다시 승인받는 문서가 아니다.

후속 실제 상태: 1~2단계·3a/3b는 구현·검증했다. 아래 목록은 최초 계획이며 실제 메타데이터 계약·Node/EJS 최소 설정·두 사이트 조회 결과는 [3b 실제 결과](PHASE_1_STAGE_3B_RESULT.md)를 따른다. 3c 선택·FPS/CFR·획득/시간축·처리 계약은 여전히 별도 실험·구현이 필요하다.

3c는 [첫 계약 단위](PHASE_1_STAGE_3C1_RESULT.md)를 구현·검증했다. QualityOption/FormatSelection은 후보 조합 검사이며 자동 순위 계산이 아니다. Best/음성 순위·FormatSelector는 [실행 전 실험 계획](PHASE_1_STAGE_3C_EXPERIMENT_PLAN.md)의 근거 확보 후 구현한다.

후속 [3c-2 실제 결과](PHASE_1_STAGE_3C2_RESULT.md): 자동 FormatSelector·최소 bitrate/transport·불변 ranking 기준을 구현·검증했다. 다섯 구간 요청에서 실패와 시간축 문제를 확인했으며 4단계 획득/처리 계약과 모드별 성공 정책은 계속 보완 대상이다. 위 미구현 상태는 3c-1 당시 기록이다.

## 1. 결과와 시간 계산

- Application `OperationResult<T>`는 성공 데이터/실패 `OperationError`/취소 중 하나만 갖는다. `ClipJobResult`는 채택한 상태·작업 ID·성공 경로·오류·주의사항·잔존 경로를 보존한다. 실패·취소에 성공 경로를 넣지 않는다.
- 오류는 코드, Input/Execution 분류, 안전한 메시지, 선택 필드명이다. 공개 결과에 원시 명령·JSON·stderr·서명 URL을 넣지 않는다. 정리 경고는 오류와 별도다. 예상 실패만 변환하고 프로그래밍 오류는 숨기지 않는다.
- 시간 필드와 FPS는 채택한 long/int를 유지한다. 중간 계산은 `System.Numerics.BigInteger`로 수행하고 저장 경계에서 long 범위를 검사한다. 이를 위해 별도 패키지·범용 프로젝트를 추가하지 않는다.
- `FrameRate.TryCreate(long numerator, long denominator, out FrameRate? value, out TimeValidationError? error)`: 양수 검사, 약분. `TimecodeInput.TryParse(string text, out TimecodeInput? value, out TimeValidationError? error)`: 자릿수·ASCII·필드 수와 long/int 저장 범위 검사. MM/SS 범위는 semantic 검사로 둔다.
- `Timecode.TryCreate(TimecodeInput input, FrameRate fps, out Timecode? value, out TimeValidationError? error)`: MM/SS와 FF 범위·표현 가능 시간 검사. `TryToTimeSpan`은 정확한 유리수에 tick을 곱한 뒤 최근접 짝수 반올림한다. 음이 아닌 연속 프레임 번호는 정확한 시간×FPS의 floor로 구하고 long 초과는 실패한다.
- 정확한 시간 분자/분모는 내부 구현에 두고 비교 시 교차 곱한다. `MediaTimeRange.TryCreate(Timecode start, Timecode end, ...)`는 같은 FPS와 Start/End 관계를 보장한다. 타임코드 표시가 연속 프레임 역변환의 항등식이라고 가정하지 않는다.
- Application의 End<=Duration은 정확한 End와 Duration tick 유리수를 비교한다. tick 반올림으로 원본 길이 초과를 허용하지 않는다. metadata Duration을 TimeSpan으로 변환한 측정 분해능은 주의사항/검사 제한에 보존한다.
- JSON 숫자는 원시 숫자 토큰을 부호·정수부·소수부·지수로 해석해 정확한 유리수로 만든다. 숫자 문자열은 거부한다. FPS는 약분 후 분자/분모가 long 안에 있어야 한다. Duration은 양수·TimeSpan 범위와 반올림을 검사한다. 해상도는 양의 정수/int 범위만 허용한다. 변환 실패는 해당 값 미확인과 필드 진단으로 처리하고 기존 누락 정책을 적용한다.
- 거대한 지수/숫자는 long/TimeSpan으로 표현 가능할 수 있는지 먼저 자리수로 검사하고 불필요하게 거대한 10의 거듭제곱을 생성하지 않는다. 29.97은 2997/100이며 30000/1001로 추정 보정하지 않는다. 0·음수 길이/FPS는 실행 가능한 값이 아니다.

## 2. ProcessRunner

후속 실제 상태: [2a 결과](PHASE_1_STAGE_2A_RESULT.md), [2b 수명 결과](PHASE_1_STAGE_2B_RESULT.md). 아래 내용은 구현 전 구체화 기록이며 2b에서 수명 후보를 실험으로 보완했다. 현재 Windows 실행은 CreateProcessW의 생성 시 JOB_LIST/HANDLE_LIST와 CRT argv 인코딩을 사용한다. root·Job 활성 0·reader 정리 확인 후 Cancelled/TimedOut을 반환하며, 자연 종료와 정리 예산·시작 rollback·EOF 구분은 실제 결과 기록을 우선한다. 하위 호출 시그니처와 byte 수집 상한은 유지한다.

정의·구현 위치는 Media.Infrastructure/ProcessExecution이다. 제안 호출은 다음과 같다.

```csharp
Task<ProcessResult> RunAsync(
    ProcessRequest request,
    Action<ProcessOutputChunk>? outputReceived,
    CancellationToken cancellationToken);
```

- Request: 절대 실행 경로, 복사한 인자 목록, 절대 작업 디렉터리, nullable Timeout, 스트림별 Full/Tail/Drain 수집 정책. shell은 사용하지 않고 ArgumentList·redirected streams·CreateNoWindow로 실행한다. 인자·수집 모델은 불변이며 callback/token은 모델 밖에 둔다.
- Result: 채택한 ProcessStatus, `int? ExitCode`, stdout/stderr 각각의 bytes·총 수신량·생략/상한 초과 플래그, `ProcessFailureKind?`, 안전한 내부 실패 정보, 종료/스트림 정리 확인 정보. 종료 미확인 상태에 성공 코드 0을 만들지 않는다. bytes와 진단은 Infrastructure 내부 사용이다.
- Full은 스트림당 32*1024*1024 원시 바이트를 허용하고 **다음 바이트부터** 상한 초과다. 상한과 BOM/개행도 수신 바이트에 포함한다. 초과 시 잘린 JSON을 반환해 파싱하지 않고 중단·정리한다. 별도 tail 복제는 필요하지 않다.
- Tail은 각 256*1024 원시 바이트 ring buffer이고 꽉 차도 계속 drain한다. 총 수신량은 checked long이며 초과를 관리 실패로 처리한다. Drain은 보관하지 않는다. 바이트 수와 UTF-16 문자열 길이를 혼용하지 않는다.
- 양 스트림을 동시에 비동기 byte read한다. 진행 출력은 상태를 가진 UTF-8 decoder와 최대 8KiB 문자 chunk로 전달하고 UTF-8 문자 중간 분할을 보존한다. JSON 전체 디코딩은 완료 후 엄격한 UTF-8로 하고 잘못된 인코딩은 metadata 실패다. tail 앞의 잘린 문자·불완전 종료 문자는 진단 생략 표시와 함께 안전하게 표시한다.
- callback은 도구 어댑터 소유의 짧은 동기 chunk 처리 함수다. 콘솔/파일 쓰기·네트워크·대기·다시 RunAsync 호출을 넣지 않는다. stdout/stderr 호출은 병행할 수 있다. 예외는 CallbackFailed로 기록하고 중단·정리한다. 임의 사용자 callback의 무한 blocking을 지원 계약으로 두지 않는다.
- 줄 단위 조립은 어댑터 책임이며 64KiB UTF-8 상당량을 넘는 진행 레코드는 해석을 중단하고 진단 표시 후 줄 끝까지 drain한다. JSON은 이 줄 제한을 사용하지 않는다. CR/LF·끝에 개행 없는 chunk를 검사한다. progress 파싱 실패만으로 조용히 실행 성공을 만들지 않고 진행 정보 불명과 처리 오류를 구분한다.
- 시작 전 이미 취소됐으면 프로세스를 만들지 않고 Cancelled다. 시작 직전 monotonic deadline을 잡는다. 시작 실패는 StartFailed이며 비동기 대기로 실행 시간을 관리한다.
- 중단 원인은 한 번만 기록하는 gate에서 결정한다. 취소/timeout/읽기 실패/상한 초과 중 처음 수락한 원인을 보존한다. 동시에 이미 관찰된 원인은 cancellation→timeout→관리 실패 순으로 정한다. 종료 코드 관찰만으로 terminal 성공을 고정하지 않는다. 스트림 완료까지 관리 실패를 수집한다.
- 중단 후 새 단계 진입을 막고 별도 10초 token으로 kill·root exit·stream drain/정리를 기다린다. 작업 취소 token을 cleanup에 전달하지 않는다. cleanup 초과/종료 증거 부족은 원래 원인과 함께 TerminationFailed이며 원래 취소/timeout보다 최종 상태에서 우선한다.
- Exited는 exit code 0과 동의어가 아니다. 자연 root 종료 후 자식이 파이프를 유지하는 경우도 무제한 EOF 대기에 두지 않고 root 관찰부터 별도 10초 정리 범위로 관리한다. 잔존 자식 확인·root 선종료 대응의 구체 수단은 E3 결과로 정한다. 제품에 tree kill만 넣고 자식 종료를 보장했다고 표현하지 않는다.

공식 근거: [Process.Kill](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0)는 root의 종료 대기가 descendant 종료 증거를 제공하지 않음을 설명한다. 실제 보완 필요 여부는 E3에서 판정한다.

## 3. 도구·미디어 경계

기존 `IToolDiagnosticsService`·Found 의미·WPF DI 연결은 그대로 유지한다. 새로운 실행 진단은 Downloader.Application의 `IToolExecutionDiagnostics`와 Infrastructure 구현을 사용한다.

```csharp
Task<OperationResult<ToolExecutionReport>> DiagnoseAsync(
    ToolDiagnosticRequest request, CancellationToken cancellationToken);
// Request는 명령에 필요한 도구·모드 기능 검사 범위를 지정한다.
```

Report에는 도구별 발견/실행/버전/기능 결과를 분리한다. 도구 하나의 실패에도 diagnose의 나머지를 검사하며 전체 실패 보고서에 성공 항목도 보존한다. `OperationResult` 실패는 보고서 생성을 못한 관리 실패에 사용하고 개별 도구 실패는 report 항목으로 반환한다. 실행 경로는 기존 locator/settings로 선택하고 명시 경로를 대체하지 않는다. JS/EJS 항목·runtime 옵션은 설치 yt-dlp 버전 확인 후 연결한다.

공유 Media.Core에는 Downloader 결과 타입을 사용하지 않는 `MediaOperationResult<T>`와 MediaFailure를 둔다. 성공/실패/취소 중 하나를 표현하고 실패에 공통 도구 실행 원인과 안전한 설명을 포함한다. System.Diagnostics.Process 객체·명령은 노출하지 않는다.

```csharp
Task<MediaOperationResult<MediaProbeResult>> ProbeAsync(
    MediaProbeRequest request, CancellationToken cancellationToken);
Task<MediaOperationResult<MediaProcessingResult>> ProcessAsync(
    MediaProcessingRequest request,
    IProgress<MediaProcessingProgress>? progress,
    CancellationToken cancellationToken);
```

- ProbeRequest: 로컬 파일 목록, Summary/FrameTimestamps 검사 수준, 필요한 경우 검사할 로컬 시간 창. ProbeResult: 파일별 format/streams, nullable duration/start timestamp, codec·stream presence·유리수 rate/time_base, 필요 창의 PTS 및 관찰 범위/제한. 기본 probe와 프레임 검사는 각각 호출당 60초다.
- 정수 PTS와 time_base를 보존하며 DTS·best_effort_timestamp·r_frame_rate·avg_frame_rate를 섞지 않는다. 선택한 PTS 필드와 부재 여부를 기록한다. summary만으로 CFR을 판정하지 않는다. frame 출력도 32MiB 상한을 적용하고 사이트 영상 전체 프레임을 덤프하지 않는다.
- MediaInput: 분리 A/V까지 가능한 로컬 파일·선택 스트림 참조·probe 측정 정보. 원본 플랫폼과 URL은 없다. 원본 시간축과의 관계는 Application의 AcquiredMedia에 둔다.
- ProcessingRequest: MediaInput, **검증된 로컬 시간 범위**, 모드·staging 경로. 여기의 범위 타입은 입력 PTS·time_base를 표현할 수 있어야 하며 Timecode 기반 MediaTimeRange를 억지 재사용하지 않는다. 정확한 타입은 E1/E2 후 결정한다.
- ProcessingResult: staging 절대경로·실제 Copy/Trim/Mux/Encode 내용·제한. 파일 생성 성공을 전체 성공으로 간주하지 않는다. 원본↔로컬 relation과 스트림별 offset·공통 출력 origin은 E1/E2 결과 없이 확정하지 않는다.

획득 계약은 `IClipDownloadService.AcquireAsync(AcquisitionRequest, IProgress<AcquisitionProgress>?, CancellationToken)` → `Task<OperationResult<AcquiredMedia>>`로 구체화한다. Request에는 소스·선택 포맷·확정된 요청 시간 구간·작업 폴더를 전달한다. 생성 파일과 실험으로 확인된 시간축 근거를 결과에 담는다. 서명 URL·쿠키는 Infrastructure 내부에만 둔다. **FPS 미확인 검사용 요청을 이것에 억지로 연결하지 않는다.** 선행 FPS 검사 계약·AcquisitionRequest의 최소 범위 확정은 E2에서 결정한다.

## 4. 출력 세션·Application 조정

```csharp
Task<OperationResult<IOutputSession>> CreateAsync(
    Guid jobId, string finalPath, CancellationToken cancellationToken);
// IOutputSession: JobId, WorkDirectory, StagingPath, FinalPath
Task<OperationResult<string>> CommitAsync();
Task<WorkspaceCleanupResult> CleanupAsync(WorkspaceCleanupMode mode);
```

- Workspace는 Y/--yes 이후 생성한다. Create는 절대경로·기존 폴더·충돌을 재검사하며 작업 전용 app data 폴더와 출력 폴더의 고유 staging 경로를 만든다. session 생성 실패도 부분 생성 자원을 정리하고 잔존 경로를 반환할 수 있도록 OperationResult의 warning/remaining path 필드를 공통으로 지원한다.
- 생성·소유한 경로만 추적한다. 획득은 session 작업 폴더 아래에만 쓴다. 사용자가 만든 파일이나 최종 경로를 정리 대상으로 glob하지 않는다. 세션 생성 때 staging 이름이 고유하더라도 실제 파일 생성 시 CreateNew/FFmpeg -n으로 덮어쓰기를 차단한다.
- Application gate는 실행/취소 수락/Finalizing을 한 지점에서 조정한다. 검사가 끝나고 gate에서 취소되지 않았음을 확인한 뒤 Finalizing으로 전이한다. 그 뒤 CommitAsync에는 작업 취소 token을 전달하지 않는다. 이미 수락한 취소라면 commit을 호출하지 않는다.
- Commit은 Prepared→Committed/CommitFailed의 한 번만 가능한 호출이다. staging→final 동일 볼륨 이동이며 overwrite=false다. 성공 뒤 늦은 취소는 성공을 유지한다. 두 번째 commit은 프로그래밍 오류로 취급하고 조용히 반복 성공하지 않는다.
- Cleanup은 DeleteOwnedTemporaryFiles 또는 PreservePotentiallyInUse를 받는다. 후자는 삭제하지 않고 잔존 경로를 반환한다. 독립 cleanup이라 token을 생략하며 중단된 사용자 token으로 정리를 건너뛰지 않는다. 파일 정리의 시간 상한은 채택한 프로세스/스트림 10초와 혼동하지 않는다.
- cleanup 결과는 제거/잔존 경로·안전한 warning이다. 반복 cleanup은 이미 제거한 항목을 오류로 만들지 않고 최종 파일을 제외한다. IDisposable만으로 정리 결과를 숨기지 않는다.
- 준비 이후 metadata 재조회에서 같은 영상·포맷인지 검사한다. 선택 FPS가 달라져 입력 의미가 달라지면 조용히 계산을 바꾸지 않고 InvalidMetadata 실행 실패로 정지한다. 최초 FPS 미확인→확인은 이미 표시한 미확인 항목의 해소로 구분한다. 품질/모드/IN/OUT 변경은 없다.

## 5. 오류 매핑 표

| 하위 원인 | Application 결과 | 분류·보존 |
|---|---|---|
| 옵션/시간 syntax·저장 범위 실패 | InvalidInput | Input, 필드명 |
| MM/SS/FF·구간·영상 길이 위반 | InvalidRange | Input, 실행 중 확인돼도 요청 자체 범위 오류는 Input |
| URL 미지원/비정상 | UnsupportedSource 또는 InvalidInput | Input |
| 출력 형식/폴더/사전 충돌 | InvalidOutputPath/OutputConflict | Input |
| 확인 후 출력 충돌 | OutputConflict | Execution |
| locator Missing / 탐색·실행 실패 | MissingTool / ProcessFailed | Execution, 탐색 원인 별도 진단 |
| StartFailed/Exited(nonzero)/읽기·callback 실패 | ProcessFailed | Execution, 도구 종료 코드 내부 보존 |
| JSON 상한/디코딩/문법·필수 ID 실패 | InvalidMetadata | Execution, 잘린 응답 파싱 금지 |
| 일반 출력 상한의 실행 관리 실패 | ProcessFailed | Execution |
| 종료 확인된 timeout | TimedOut | Execution |
| root/child/stream 종료 미확인 | ProcessTerminationFailed | Execution, 원래 원인과 잔존 파일 보존 |
| 종료 확인된 사용자 취소 | Cancelled | 오류 없음, cleanup warning 분리 |
| 접근 불가/완료 영상 아님 | SourceUnavailable | Execution, live/scheduled/unknown 원인 구분 |
| 포맷 없음/모드 조합 미지원 | UnsupportedFormat | Execution, 다른 화질·모드 대체 없음 |
| 검사 후 FPS 미확인 | UnknownFrameRate | Execution |
| 로컬 입력/결과 미디어 조건 실패 | VerificationFailed | Execution, 성공 경로 없음 |
| 쓰기·이동·파일 생성 실패 | OutputFailed | Execution |
| 프로세스 종료 후 파일 정리 실패 | 원래 결과 유지 | warning·remaining paths, Cancelled면 130 유지 |

사이트 어댑터는 generic nonzero를 stderr 단어 하나로 SourceUnavailable로 단정하지 않는다. 확인 가능한 extractor 오류·구조적 근거가 없으면 ProcessFailed를 유지하고 안전한 진단을 붙인다. 정확도 근거 없음은 검사 실패/UnsupportedFormat의 적용 시점을 전략 확정 때 구체화한다.

## 6. 단계별 파일·커밋 단위 계획

모든 경로는 저장소 기준이며 **계획된 파일**이다. 모델 하나마다 기계적으로 commit하지 않고 책임과 테스트를 묶는다. 밀접한 enum/result는 한 파일에 묶을 수 있다.

| 단계·단위 | 제품 파일 계획 | 대응 검증 |
|---|---|---|
| 1a FPS·시간 | src/ClipMaker.Media.Core/ValueObjects/{FrameRate,TimecodeInput,Timecode,TimeValidationError}.cs | tests/ClipMaker.Media.Core.Tests/{FrameRateTests,TimecodeTests}.cs 및 실제 로직과 함께 테스트 csproj |
| 1b 구간 | 같은 ValueObjects/MediaTimeRange.cs | MediaTimeRangeTests.cs; exact 비교·짝수 반올림·범위 초과 |
| 2a 실행·출력 | src/ClipMaker.Media.Infrastructure/ProcessExecution/{IProcessRunner,ProcessModels,ProcessRunner}.cs | 기존 Media.Infrastructure.Tests/ProcessRunnerTests.cs·실행 fixture |
| 2b 취소·timeout | ProcessRunner.cs·필요 최소 수명 helper | 동시 출력·root 선종료·자식·cleanup tests; E3 반영 |
| 2c 진단 | Application/Contracts/IToolExecutionDiagnostics.cs, Models/ToolExecutionReport.cs; Downloader.Infrastructure/Diagnostics/ToolExecutionDiagnostics.cs | 필요한 Downloader.Infrastructure.Tests 생성·진단 tests |
| 3a URL | Downloader.Infrastructure/Sources/SupportedVideoSourceParser.cs; Application/Models/VideoSourceReference.cs | URL 정규화·ID·중복 v·포트·escape/Uri 경계 |
| 3b JSON | Application/Models/{VideoInfo,VideoFormat,OperationResult,OperationError}.cs, Contracts/IVideoInfoService.cs; Infrastructure/YtDlp/{YtDlpVideoInfoService,YtDlpMetadataParser}.cs | 익명화 fixtures·숫자·누락·중복 ID·상한 |
| 3c 선택 | Application/Models/{QualityOption,FormatSelection}.cs, Validation/FormatSelector.cs | 필요한 Application.Tests 생성·선택 tests; E2 후 순위 확정 |
| 4a 획득 | Application/Contracts/IClipDownloadService.cs, Models/AcquiredMedia.cs; Infrastructure/Downloads/YtDlpClipDownloadService.cs | 사이트 시간축·분리 A/V; 실험 의존 타입 같이 연결 |
| 4b 검사 | Media.Core/Contracts/IMediaProbe.cs, Models/{MediaInput,MediaProbeModels,MediaOperationResult}.cs; Media.Infrastructure/FFprobe/FfprobeMediaProbe.cs | summary·PTS·누락·코덱·범위 tests |
| 4c 처리 | Media.Core/Contracts/IMediaProcessingService.cs, Models/MediaProcessingModels.cs; Media.Infrastructure/FFmpeg/FfmpegMediaProcessingService.cs | E1·E2 기반 Fast/Accurate·staging 검사 |
| 5a 세션 | Application/Contracts/{IOutputWorkspace,IOutputSession}.cs, Models/WorkspaceResults.cs; Infrastructure/FileSystem/OutputWorkspace.cs·OutputSession.cs | 경로·쓰기·충돌·한 번 확정·cleanup |
| 5b 준비 | Application/Contracts/IClipJobService.cs, Models/{ClipRequest,PreparedClip,ClipJob,ClipJobResult,JobProgress}.cs; UseCases/ClipJobService.cs | 준비 시 쓰기 없음·누락·구간·소스 일치 |
| 5c 조정 | ClipJobService.cs와 필요한 validation helper | 실행 재검사·단계 실패·취소·최종 확정 gate |
| 6a 호스트 | apps/ClipDownloader.Cli/{Program,CommandLineParser}.cs·csproj, Composition/{ServiceRegistration,LoggingSetup}.cs; ClipMaker.slnx CLI 등록 | 명령·help·도구 옵션·잘못된 입력 |
| 6b 대화·수명 | CLI/{ConsoleInteraction,ConsoleProgress}.cs 및 Program.cs | Y/N·EOF·비대화형·--yes·Ctrl+C·로그·DI 수명 |
| 6c 통합 | tests의 필요한 CLI 통합 검사 및 상세 설계 실제 결과 | 솔루션·관련 tests·양 사이트·도구 누락/실패·WPF 회귀 |

실제 테스트 프로젝트는 필요한 단계에서 생성·솔루션 등록한다. test helper는 해당 테스트 안에 두고 검증용 프로세스 fixture의 빌드 방법은 E3 결과로 최소화한다. Phase 0 WPF 제품 파일은 기능 확장을 위해 변경할 계획이 없다. 제품 패키지·테스트 패키지 버전을 유지하고 위 파일 수를 목표로 삼지 않는다.

## 7. 실험 이후에만 확정할 항목

원본↔로컬 relation 타입·증거 수준, stream별 offset과 로컬 범위 타입, FPS 사전/실행 검사 순서, Best 정렬, Fast 허용 코덱, Accurate 인코더 설정·VFR 제한·A/V 기준, 자식 종료 보완은 [실험 실행 절차](PHASE_1_EXPERIMENT_RUNBOOK.md)의 결과로 결정한다. yt-dlp 설치는 사용자가 담당하며 설치 완료·경로·버전은 아직 확인하지 않았다. 본 계약 구체화만으로 전체 설계 확정·구현 착수를 선언하지 않는다.
