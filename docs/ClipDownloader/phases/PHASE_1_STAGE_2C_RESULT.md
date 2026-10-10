# Phase 1 / 2단계 세 번째 커밋 단위 — 도구 실행 진단

수행일: 2026-10-10 (Asia/Seoul).

상태: **2c 구현·자동/실제 설치 도구 검증 완료, 커밋 대기**. 2a~2c의 현재 대상 검증을 완료했다. 실제 사이트·YouTube JS/EJS·인코딩/시간축·Application/CLI 통합과 Phase 1 필수 완료 조건은 남아 있다.

## 시작·변경 범위

- branch `phase/1`, HEAD `f697f70a12a452795735ce85a289c88129d68cd1`: `feat: Add verified process cancellation and timeout cleanup`. 시작 작업 트리는 깨끗했다.
- Application에 IToolExecutionDiagnostics, ToolDiagnosticRequest/ToolExecutionReport 및 채택한 OperationResult/OperationError를 추가했다. generic result는 성공 데이터/오류/취소 중 하나만 보존하며 이번 서비스는 조회·준비에 사용할 참조형 데이터 계약이다.
- Downloader.Infrastructure에 ToolExecutionDiagnostics·내부 ToolOutputParser를 추가했다. 실제 로직에 대응하는 Downloader.Infrastructure.Tests를 생성·솔루션에 등록했다. Phase 0의 xunit.v3.mtp-off 4.0.1·runner 4.0.0·Test SDK 18.10.1을 재사용했다. 신규 제품 패키지는 없다.
- 기존 IToolDiagnosticsService/ToolDiagnostic/ToolDiagnosticsService·ToolLocator·ProcessRunner·WPF/Composition은 수정하지 않았다. Git staging/commit/branch/tag 변경도 없다.

## 호출·진단 의미

| 요청 | 현재 검사 범위 |
|---|---|
| Diagnose | yt-dlp·FFmpeg·ffprobe 경로·실행·버전 |
| Info | yt-dlp 경로·실행·버전만 |
| Clip | 세 도구 경로·실행·버전 + 호출자가 명시한 FFmpeg encoder/muxer 요구 |

`DiagnoseAsync`는 OperationResult<ToolExecutionReport>를 반환한다. report 생성 성공과 모든 검사의 통과는 다르다. 예를 들어 하나가 Missing이어도 Succeeded report에 세 결과가 남고 AllRequestedChecksPassed=false다. 향후 CLI는 이 값을 기준으로 diagnose 성공/실패를 매핑해야 한다. 취소는 Cancelled이고 종료 확인 실패는 report 생성 실패의 ProcessTerminationFailed로 반환해 추가 호출을 중단한다.

경로 Found, 진단 실행 Passed, 버전 Passed, 요청 기능 Passed를 분리했다. 도구가 발견되어도 실행에 실패할 수 있고 exit 0이어도 버전 응답이 잘못되면 VersionStatus=Failed다. 요구된 도구가 빠지거나 중복된 report를 만들지 못하게 모델 생성에서 검사하며 빈 목록의 vacuous success를 허용하지 않는다.

Clip의 기능 목록은 **호출자 요구**다. 현재 clip mode/화질/처리 전략을 추정해 libx264/aac를 강제 선택하지 않는다. 필요한 기능을 명시하지 않은 요청의 AllRequestedChecksPassed=true는 기본 도구 진단 통과만 의미한다. 해당 모드 실행 적합성은 이후 처리 전략에 맞는 요구 목록과 실행·결과 검사로 확인한다.

## 실행·파싱·실패 처리

- 기존 locator/settings의 명시 경로→tools→PATH 정책을 사용한다. 명시 경로 누락/오류에 대체가 없고, 발견 이후 실행 실패에도 다른 경로/전체 작업 재시도를 하지 않는다. 호출마다 경로/버전을 다시 확인하며 서비스에 지난 결과를 cache하지 않는다.
- ProcessRunner 호출당 timeout은 15초다. stdout Full(32MiB), stderr Tail(256KiB)를 사용하며 full 수집 완료·EOF·상한을 확인한 후 엄격한 UTF-8로 해석한다. 잘린·overflow·비정상 인코딩 응답을 정상 버전/기능으로 해석하지 않는다.
- yt-dlp 인자는 `--ignore-config --version`이며 사용자 config의 부가 동작을 읽지 않는다. FFmpeg/ffprobe는 `-version`, 기능은 FFmpeg의 `-hide_banner -encoders/-muxers`다. 버전 조회에는 영상 URL·쿠키·인증값·remote component/도구 설치가 없다.
- yt-dlp는 유효한 YYYY.MM.DD 및 해당 nightly suffix를, FFmpeg/ffprobe는 각 도구를 식별하는 version header·bounded token을 검사한다. unknown·너무 긴 버전·다른 도구 header·임의 원시 출력을 확인된 버전으로 전달하지 않는다.
- encoder/muxer는 알려진 table header/flags와 정확한 이름으로 확인한다. libx264rgb가 libx264를 대신하지 않고 D-only 포맷은 muxer로 통과하지 않는다. 별칭은 분리하며 FFmpeg 8의 Formats 헤더와 device marker, 이전 File formats 헤더를 샘플·실제 출력에 따라 처리한다.
- 기능 확인은 **도구가 목록에 선언한 기능**의 확인이다. 실제 encoder 초기화·MP4 생성·A/V/구간 정확도·하드웨어 가용성의 성공 기록이 아니다.
- normal StartFailed/nonzero/읽기·capture 실패는 해당 도구의 ProcessFailed, timeout은 TimedOut, 없는 도구는 MissingTool, 없는 요청 기능은 UnsupportedFormat으로 분류한다. 모두 Execution 분류다. 일반 실패에는 다른 필수 도구를 계속 검사한다.
- 프로세스·자식·reader 종료 미확인은 ProcessTerminationFailed로 즉시 중단하며 정상 Cancelled를 주장하지 않는다. 이미 취소된 입력·조회 후 취소에는 실행 단계에 진입하지 않는다. 예상하지 않은 프로그래밍 오류를 catch-all로 일반 오류에 숨기지 않는다.
- 공개 결과에 Process 타입·원시 stdout/stderr/예외/명령을 넣지 않는다. 경로 실패 원문도 안전한 설명으로 투영한다. CLI 로그 소유권/진단 로그 연결은 6단계에서 구성하며 이번에는 원시 출력을 자동 저장하지 않았다.

공식 옵션 근거는 [FFmpeg generic options](https://ffmpeg.org/ffmpeg.html#Generic-options), [yt-dlp configuration](https://github.com/yt-dlp/yt-dlp#configuration)과 설치 버전 출력으로 대조했다. 목록 파싱 형식은 공식 옵션 설명만으로 가정하지 않고 실제 8.1 출력과 테스트 fixture를 사용했다.

## 실제 설치 도구 통합 검증

솔루션에 등록하지 않은 `.phase2c-validation-7b8108cb`의 패키지 없는 임시 console harness가 실제 locator/settings·제품 진단 서비스·2b ProcessRunner를 호출했다. 이는 제품 CLI 구현이 아니다. 실행 파일 위치는 사용자 설치 경로이며 자동 설치/수정하지 않았다.

| 도구 | 명시 경로 | 실제 버전·결과 |
|---|---|---|
| yt-dlp | `C:/Users/shiro/AppData/Local/Microsoft/WinGet/Packages/yt-dlp.yt-dlp_Microsoft.Winget.Source_8wekyb3d8bbwe/yt-dlp.exe` | 2026.08.19; Found/Execution/Version 통과 |
| FFmpeg | `C:/Users/shiro/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-8.1-full_build/bin/ffmpeg.exe` | 8.1-full_build-www.gyan.dev; Found/Execution/Version 통과 |
| ffprobe | 같은 FFmpeg bin의 `ffprobe.exe` | 8.1-full_build-www.gyan.dev; Found/Execution/Version 통과 |

| 검증 요청 | 관찰·실제 판정 |
|---|---|
| Diagnose | 세 도구 모두 검사, Succeeded report·AllRequestedChecksPassed=true |
| Info | yt-dlp만 검사, Succeeded·true |
| Clip + libx264/aac/mp4 요구 | 세 버전과 encoder/muxer 목록 검사, 각 요구 통과·Succeeded·true |
| 명시한 yt-dlp 경로가 없는 Diagnose | yt-dlp Missing/MissingTool, FFmpeg/ffprobe 버전 검사는 통과; Succeeded report·AllRequestedChecksPassed=false; 대체 실행 없음 |

첫 harness 실행과 누락 경로 사례를 추가한 후속 실행은 모두 exit 0이었다. 후속 실행의 exit 0은 harness가 기대한 MissingTool/다른 도구 검사 지속 조건을 충족했다는 뜻이며 누락 도구가 정상이라는 뜻이 아니다.

임시 root의 정확한 절대경로·workspace 내부 경계·reparse point 부재를 확인한 뒤 해당 root만 정리했다. Test-Path=false를 확인했다. 설치 파일·기존 docs·제품 파일·사용자 로그를 삭제하지 않았다. 실제 사이트 조회·메타데이터 획득·영상 생성/다운로드는 하지 않았다.

## 최종 자동 검증

| 명령·대상 | 실제 결과 |
|---|---|
| `dotnet build ClipMaker.slnx --no-restore --verbosity minimal` (승인된 실행) | exit 0, 경고 0·오류 0 |
| `dotnet test ClipMaker.slnx --no-build --verbosity minimal` (승인된 실행) | exit 0; Core 85·Media.Infrastructure 73·Downloader.Infrastructure 46, 총 204 통과; 실패/건너뜀 0 |
| 신규 46개 | 진단 흐름 32·모델 불변조건 7·버전/기능 경계 7; fake locator/runner로 설치 도구·네트워크와 독립 |
| 기존 WPF 파일 진단 회귀 | 실행 불가인 세 임시 파일도 기존 서비스에서는 Found; 파일 존재 의미를 유지함 |
| `git diff --check`·신규 파일 whitespace 확인 | 오류 없음 |

테스트는 명령별 범위, 실패 격리, 명시 경로 대체/재시도 금지, 15초/수집 정책, 잘못된 버전·달력 날짜·UTF-8/잘림, 정확한 feature/alias/flags, 목록 상한 초과, 취소/종료 미확인의 호출 중단, refresh·불변 snapshot·프로그래밍 오류 전파·원시 진단 비노출을 포함한다. 자동 테스트 통과와 실제 설치 도구 검증은 별도로 기록했다.

## 커밋 지점·다음 작업

권고 메시지: `feat: Add tool execution and capability diagnostics`.

커밋 후보: Application 계약/모델 3개, Downloader.Infrastructure 진단/파서 2개, Downloader.Infrastructure.Tests csproj/테스트 4개, ClipMaker.slnx 등록, 본 결과·상세 설계·Baseline 상태 기록이다. 실제 staging/commit은 하지 않았으며 여기서 멈춘다.

다음은 **3a 공식 URL 검증·정규화**다. YouTube JS runtime/EJS·실제 조회, Best/코덱 전략·FPS 확인 순서·획득 시간축은 실험 의존으로 유지한다. 세 도구 버전과 libx264/aac/mp4 목록 통과만으로 해당 실험이나 Phase 1 전체 완료를 선언하지 않는다. Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.
