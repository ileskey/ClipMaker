# Phase 1 / 2단계 두 번째 커밋 단위 — 취소·타임아웃·프로세스 수명

수행일: 2026-10-10 (Asia/Seoul).

상태: **2b 구현·관련 실제 프로세스/자동 검증 완료, 커밋 대기**. 2단계 전체 완료가 아니다. 다음은 2c 실행 진단이다. 실제 사이트·미디어 시간축·포맷 실험과 Phase 1 필수 완료 검증은 남아 있다.

## 시작 상태·환경

- branch `phase/1`, 시작 HEAD `45daa40cad6452322d073076bfe14c4ee7fa630b`: `feat: Add bounded process execution and output capture`. 작업 트리는 깨끗했다.
- Windows 11 Pro 10.0.26200 x64, SDK 10.0.302·테스트 runtime .NET 10.0.10. Windows 버전은 승인된 `Get-CimInstance Win32_OperatingSystem` 조회로 확인했다. 최초 sandbox CIM 조회는 접근 차단으로 실패했으며 환경 확인 성공 근거가 아니다.
- 기존 변경·commit/tag는 보존했고 staging/commit/branch/tag 조작은 하지 않았다. 패키지·제품 프로젝트·WPF 기능·실제 사이트 명령·영상 다운로드를 추가하지 않았다.

## E3 실제 관찰·전략 결정

패키지 없는 기존 fixture에 root/child/grandchild, root 선종료, 중간 부모까지 선종료, root 출력과 분리된 silent child 사례를 추가했다. GUID 임시 디렉터리에 PID와 시작 UTC ticks를 기록하고 별도 observer가 생존을 확인했다. 정리 대상은 이 실험이 생성한 동일 PID·시작 ticks로 한정했다.

| baseline 사례 | 실제 관찰 |
|---|---|
| 살아 있는 root·child·grandchild에서 tree kill | 세 PID 종료와 stdout/stderr EOF 확인 |
| root 0 종료 뒤 직접 child가 파이프 유지 | root 완료가 자식/파이프 완료 증거가 아니었음. 2a의 root.HasExited guard는 종료 요청을 건너뜀 |
| root와 중간 부모가 종료되고 grandchild만 생존 | 종료된 root의 `Kill(true)`로 grandchild를 찾지 못함. 기록된 grandchild 생존·열린 stdout 확인, observer 정리 후 EOF |

초기 실험에서는 ‘종료된 root에 tree kill을 호출하면 직접 child도 항상 남는다’는 가정으로 작성한 assertion이 실패했다. 이 환경에서는 직접 child가 종료될 수 있었다. 잘못된 가정을 폐기하고 root 완료/pipe 미완료와 **중간 부모 선종료**를 구분해 재현했다. 수정한 baseline 3개가 통과했다. 초기 실패를 성공 기록으로 바꾸지 않는다.

이 증거에 따라 호출별 Windows Job Object를 도입했다. root가 실행된 뒤 job에 연결하는 방식은 연결 전 자식 생성 경합이 있으므로 `PROC_THREAD_ATTRIBUTE_JOB_LIST`로 **프로세스 생성 자체에 job 연결**을 포함했다. job 생성/연결이 실패하면 StartFailed이며 추적 없는 실행 fallback은 없다.

공식 근거: [Process.Kill](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0)의 root/descendant 종료 관찰 차이, [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)의 그룹 관리, [UpdateProcThreadAttribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)의 JOB_LIST/HANDLE_LIST, [QueryInformationJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-queryinformationjobobject)의 job 상태 조회를 대조했다. 실제 재현 결과와 공식 API 설명을 구분한다.

## 구현·현재 계약

- 기존 RunAsync·ProcessRequest·ProcessResult 공개 시그니처는 유지한다. 플랫폼 소유권을 내부 IProcessSession/WindowsProcessSession으로 분리했다. 기본 구현은 Windows 10 이상이며 그 외 플랫폼은 StartFailed로 명시 실패한다. net10.0 TFM이 다른 OS 지원을 보장하지 않는 기존 범위를 유지한다.
- invocation 전용 Job Object에 kill-on-close를 설정하고 breakaway를 허용하지 않는다. 종료 요청은 TerminateJobObject로 해당 그룹에만 보낸다. root·계속 살아 있는 구성원의 종료는 root 프로세스 핸들과 job ActiveProcesses==0을 각각 관찰한다.
- 네이티브 CreateProcessW 경로는 shell을 사용하지 않는다. 이전 ProcessStartInfo.ArgumentList 호출 대신 내부 WindowsCommandLine이 불변 argv를 Windows CRT 규칙으로 인코딩한다. 빈 값·공백·따옴표·따옴표 앞/끝 backslash·Unicode·tab/newline·shell metacharacters 보존을 실제 fixture로 검증했다. executable 절대경로는 applicationName으로 별도 전달한다.
- 두 출력은 private async named pipe로 동시에 읽는다. HANDLE_LIST에는 이 호출의 stdin/stdout/stderr만 넣어 동시 실행 간 pipe handle 상속을 차단한다. stdin은 NUL로 EOF를 전달하고 호스트의 확인 입력을 도구가 읽지 않게 한다. CreateNoWindow에 해당하는 네이티브 플래그를 유지한다.
- SafeHandle과 registered wait로 process/thread/job/pipe/attribute 자원 소유권을 명시한다. 프로세스 핸들을 빌리는 wait wrapper는 별도 OS event를 만들거나 프로세스 핸들을 이중 해제하지 않는다.
- native 생성 직후 초기화 실패에도 그룹 종료를 요청하고 별도 10초 안에 root·Job 0을 확인한 뒤 StartFailed로 반환한다. rollback 종료 확인 실패는 ProcessStartCleanupException의 관찰 정보를 보존해 TerminationFailed로 반환한다. job handle close 자체를 종료 증거로 사용하지 않는다. 생성 이후 실패 seam은 인스턴스/호출별 내부 테스트 인자이며 CLI·환경 설정 플래그가 아니다.
- ProcessRunState의 한 gate에서 첫 중단 원인을 보존한다. 동시에 관찰됐으면 cancellation→timeout→관리 실패 우선순위이며 한 번 수락한 원인을 늦은 요청으로 덮지 않는다. 완료 gate 뒤의 취소·출력은 최종 결과를 바꾸거나 callback을 새로 호출하지 않는다.
- 시작 전 취소는 생성하지 않는다. 시작 판단 이후 취소는 cleanup이 필요하다. timeout은 시작 직전부터 측정해 startup도 포함하며 종료 대기와 별도다. 동기 OS startup 호출을 강제로 끊는 hard real-time 보장을 주장하지 않는다.
- 2a의 실행 중 취소/timeout을 무조건 TerminationFailed로 반환하던 임시 정책을 제거했다. 정상 Cancelled/TimedOut은 **root 종료·Job 활성 0·reader 정리**를 모두 확인한 뒤 반환한다. root/Job/reader 확인 또는 종료 요청에 실패하면 원래 원인을 보존하고 최종 TerminationFailed를 반환한다.
- 원시 Full/Tail/Drain·byte 상한·UTF-8 chunk 정책은 유지했다. StreamsDrained는 reader 작업의 정리 완료를 뜻하고 출력별 ReachedEndOfStream/IsComplete는 실제 EOF·수집 유효성을 별도로 표현한다. 읽기 실패 후 pipe를 닫아도 부분 수집을 완전한 JSON으로 표시하지 않는다.
- cleanup에는 이미 취소된 작업 token을 재사용하지 않는다. 기본 총 예산은 기존 **10초**다. 읽기 token은 source 해제 전 보존하여 deadline 뒤 늦은 읽기가 해제된 CTS에서 Token을 다시 가져오지 않는다. 늦은 reader fault는 관찰하고 callback/result 반영은 차단한다.

## 자연 종료·정리 예산 보완

전체 회귀에서 정상 fixture가 root 종료 직후 Job 구성원 잔존으로 간헐적 ExecutionFailed가 됐다. 16개 동시 자연 종료 사례로 재현했다. 순간적인 accounting/PID 목록·조회 불가 항목을 살아 있는 장기 자식으로 단정하는 방식은 불충분했다. 일부 항목의 image 조회는 접근 거부여서 종류를 확정하지 않았다. PID/이름 기반 특별 예외나 allowlist는 넣지 않았다.

현재는 root가 자연 종료한 뒤 Job 그룹이 자연 종료할 시간을 동일 cleanup 예산 안에서 제공한다. **10초 중 앞쪽 최대 5초**는 자연 그룹 종료를 기다리고, 계속 활성인 그룹은 ProcessWaitFailed를 수락해 남은 예산에 종료 요청·Job 0·reader 정리를 확인한다. 취소/timeout/출력 실패에는 이 자연 종료 유예 없이 바로 종료를 요청한다. pipeline만 남은 경우도 총 cleanup deadline을 넘지 않는다.

활성 0과 reader 정리가 자연스럽게 확인되면 Exited이며 root 코드 0/7을 그대로 보존한다. 장기 child를 강제 종료해야 했지만 정리는 확인되면 ExecutionFailed, 종료 확인이 끝내 불가능하면 TerminationFailed다. 총 cleanup 10초 정책을 늘리거나 재승인받은 것이 아니라 실험에서 확인한 종료 전이 문제에 대한 내부 예산 배분이다.

## 실제 검증

| 실행 | 실제 결과 |
|---|---|
| E3 baseline 첫 실행 | 1 통과·1 실패. 잘못된 직접 child tree-kill 가정 수정 |
| 수정 E3 baseline 실행 | 3 통과·0 실패 |
| 중간 빌드 경고 | 최초 fixture의 xUnit cancellation-token 경고 2개는 WaitAsync token 연결로 수정. 후속 native rollback 테스트의 CA1416 경고 1개는 Windows 전용 테스트 지원 범위 표기로 수정 |
| 초기 Job 적용 기본 테스트 | 2 실패: `--no-build`가 이전 DescendantExitConfirmed=null 기대값의 binary를 사용함. 수정 테스트 재빌드 후 검증에 반영 |
| 중간 전체/동시 종료 검증 | root 전이 오판 및 늦은 reader token 수명 실패를 발견. 임시 PID 관찰 전략을 제거하고 예산 내 자연 종료 대기·token 보존으로 수정 |
| 최종 `dotnet build ClipMaker.slnx --no-restore --verbosity minimal` (승인된 실행) | 종료 코드 0, 경고 0·오류 0 |
| 최종 `dotnet test ClipMaker.slnx --no-build --verbosity minimal` (승인된 실행) | 종료 코드 0; Core 85·Infrastructure 73, 총 158개, 실패/건너뜀 0 |
| 새 검증 수 | baseline 3 + 상태 gate 6 + 수명/실패/IO 19 = 28; 기존 2a·탐색 45개도 통과 |
| 승인된 fixture/testhost 잔존 조회 | 결과 없음. 해당 Get-Process 조회의 종료 코드 1은 이름 일치 프로세스 무발견이며 테스트 실패가 아님 |
| `git diff --check`·신규 파일 whitespace 검사 | 오류 없음; 일부 기존 파일의 LF→CRLF 안내만 있었음 |

최종 테스트는 실제 root/child/grandchild 취소·반복 취소·timeout, 직접/중간 부모 선종료·silent child, Job 구성원/PID별 실제 종료, 파이프 EOF, concurrent handle 격리, 16개 concurrent 자연 종료, startup timeout/cancellation 원인 보존, 종료 요청·accounting 실패 주입, 읽기 실패와 늦은 취소, reader deadline·늦은 callback 차단, stdin EOF, argv 보존, 생성 후 초기화 실패의 실제 tree rollback 및 rollback 미확인 상태 매핑을 포함한다. fake session의 짧은 테스트 deadline은 인스턴스 내부 seam이며 제품의 10초 상수를 바꾸는 환경 플래그가 아니다.

한계: OS 보안/Job 정책상 생성 자체가 실패하는 환경은 fallback 없이 실패한다. 실제 yt-dlp·FFmpeg 프로세스의 Job 동작·실행 진단은 2c와 이후 사이트 검증에서 확인해야 한다. WPF 실제 실행·사이트 조회/영상 다운로드·미디어 시간축 검증은 이번에 수행하지 않았다. 임의 callback 무한 blocking은 여전히 지원 계약 밖이다.

## 커밋 지점·다음 단위

권고 메시지: `feat: Add verified process cancellation and timeout cleanup`.

커밋 후보는 ProcessRunner 변경, 내부 session/gate/Windows 실행·argv helper·startup rollback 예외 5개, fixture 확장·기존 runner 테스트 기대값/argv 갱신, 새 baseline/lifetime/gate 테스트 3개, 본 실제 결과·상세 설계·Baseline·연결 계약/실험 기록이다. 패키지/솔루션/ToolLocator/WPF 코드 변경은 없다. 실제 staging/commit은 하지 않았으며 여기서 멈춘다.

다음은 **2c 도구 실행 진단**이다. Phase 1 전체 설계의 시간축/FPS/Best/코덱·실제 사이트 관련 실험 의존 항목 및 Completion/tag는 미확정이다.
