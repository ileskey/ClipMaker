# Phase 1 / 2단계 첫 커밋 단위 — 프로세스 실행·출력

수행일: 2026-10-10 (Asia/Seoul).

상태: **2a 구현·관련 자동 검증 완료, 커밋 대기**. 2단계 전체 완료가 아니다. 2b 취소·타임아웃·자식 수명 검증과 2c 도구 실행 진단은 남아 있다.

## 시작 상태

- branch `phase/1`, HEAD `6c10f24e0ae07518149a3c3152bccf194ef55291` (`feat: Add exact media time range validation`). 시작 작업 트리는 깨끗했다.
- 현재 이력에서 1a는 `4d92f80`, 준비 문서 commit은 `e0bbec6`, Phase 0 관련 commit은 `1ca2547`로 관찰된다. 이전 결과 문서의 commit 값은 당시 관찰이며 현재 관찰과 구분한다. 사용자 지정 원래 Base/tag `0759dc52475538df848f765ce30c005cf755107f`는 그대로 보존했다. tag 이동·이력 수정은 하지 않았다.
- staging/commit/tag/branch 변경은 이번에 수행하지 않았다. 사이트 조회·도구 설치·영상 다운로드·Phase 2·WPF 다운로드 UI·ClipEditor 작업은 없다.

## 구현

- Media.Infrastructure/ProcessExecution에 IProcessRunner, ProcessRequest/Result·출력/실패 모델, ProcessRunner, 내부 ProcessOutputCollector를 추가했다. 사이트/JSON/코덱 의미는 해석하지 않는다.
- Request는 절대 실행/작업 경로·인자 snapshot·스트림별 Full/Tail/Drain·nullable timeout이다. shell=false, ArgumentList, CreateNoWindow를 사용한다. 잘못된 구성은 예외로 처리하고 실제 시작 실패는 StartFailed와 nullable exit code로 반환한다.
- 실제 프로세스의 stdout/stderr를 byte stream으로 동시에 비동기 drain한다. 종료 코드 7도 Exited로 반환하며 어댑터가 도구 의미를 해석한다. 원시 출력·내부 예외를 Application에 전달하거나 자동 로그에 복제하지 않는다.
- Full은 스트림별 32MiB까지 보존한다. +1byte부터 OutputLimitExceeded 실행 실패이며 반환 bytes를 비워 잘린 JSON을 파싱하지 않게 한다. Tail은 256KiB ring buffer이며 오래된 bytes를 버려도 계속 읽는다. Drain은 수신 바이트 수만 기록한다. BOM/개행/invalid UTF-8도 원시 byte 수에 포함한다.
- 완전한 원시 수집, 상한 초과, EOF 도달, root 종료는 다른 정보다. EOF는 full capture 유효성과 분리한다. 특히 상한 초과 후 EOF가 와도 해당 full 출력은 IsComplete=false다.
- callback은 스트림별 UTF-8 decoder로 문자의 read 경계 분할을 보존하고 최대 8192 UTF-16 code unit chunk로 전달한다. chunk 분할 시 surrogate pair를 나누지 않는다. 잘못된 UTF-8의 callback 표시는 replacement를 사용한다. JSON 어댑터는 Full bytes를 엄격하게 디코딩해야 하며 callback 문자를 JSON 원본으로 삼지 않는다.
- callback은 짧고 비차단인 도구별 해석 함수이며 양 스트림에서 병행 호출될 수 있다. throw는 CallbackFailed로 보고하고 정리한다. newline 조립·진행 레코드 64KiB 제한·progress 의미 해석은 이후 도구 어댑터 책임이다.
- 실패 중 무한 파이프 대기·프로세스 잔존을 피할 최소 kill/tree 요청·root/EOF 관찰·별도 10초 cleanup을 연결했다. 프로세스 시작 전 이미 취소되면 시작하지 않는다. timeout 측정은 시작 직전에 시작하며 cleanup은 사용자 취소 token과 분리한다.

## 2b까지 남는 수명 제한

- 2a는 생성한 root와 파이프를 관찰하지만 자식 프로세스 목록·종료 증거를 수집하지 않는다. 실행 이후 결과의 DescendantExitConfirmed는 null이다. root/EOF 확인만으로 자식 종료를 보장하지 않는다.
- 따라서 **실행 중 취소 또는 timeout은 원래 원인을 보존하되 현재 TerminationFailed와 자식 미확인 cleanup 사유를 반환**한다. 정상 Cancelled/TimedOut을 주장하지 않는 임시 보수 정책이다. 실행 전 취소는 프로세스가 없으므로 Cancelled다. 이 제한을 최종 Application/CLI 취소 계약으로 확정하지 않는다.
- 작업 실패 시의 종료 요청과 cleanup은 출력 실패에 필요한 최소 구현이다. 정상 취소/timeout 상태 반환, root 선종료·자식/손자·파이프 유지, 중단 원인 경합·cleanup 실패·읽기 예외 주입은 2b/E3에서 검증·보완한다. 테스트에서 양 스트림 root fixture를 정리했다는 사실을 사이트 도구 tree의 종료 보장으로 확대하지 않는다.
- 임의 callback의 무한 blocking은 지원 계약이 아니다. cleanup deadline 이후 취소된 읽기 작업의 최종 수명·자식 보존 판단도 2b에서 검증한다.

## 검증용 fixture

기존 Media.Infrastructure.Tests 아래 Fixtures/ProcessFixture에 패키지 없는 net10.0 console 프로젝트를 두었다. 실제 프로세스·파이프·인자 검증에 필요하며 제품 솔루션의 실행 앱으로 등록하지 않았다. 테스트 project reference로 빌드하고 해당 빌드 출력의 ProcessFixture 폴더로 실행 파일/runtime 설정을 복사한다. fixture 소스는 테스트 프로젝트의 Compile 항목에서 제외한다. 테스트 임시 폴더는 GUID별로 격리하고 해당 테스트가 생성한 폴더만 정리한다.

기존 테스트 패키지 버전은 유지했다. 내부 byte collector를 검증하기 위해 Infrastructure 프로젝트에 테스트 assembly에 한정한 InternalsVisibleTo를 추가했다. 새 제품 패키지는 없다.

## 실제 검증 결과

| 명령·확인 | 실제 결과 |
|---|---|
| `dotnet build ClipMaker.slnx --verbosity minimal` (승인된 제한 밖 실행) | 종료 코드 0, 경고 0·오류 0; 패키지 없는 fixture 복원·빌드 성공 |
| `dotnet test ClipMaker.slnx --no-build --verbosity minimal` (승인된 제한 밖 실행) | 종료 코드 0; Core 85개, Infrastructure 45개 통과; 실패/건너뜀 0 |
| Infrastructure 분해 | 기존 탐색 22개 + 새 출력 collector 5개 + 새 ProcessRunner 18개 = 45개 |
| 검증 프로세스 잔존 조회 | 테스트 종료 뒤 ClipMaker.ProcessFixture/testhost/Infrastructure 테스트 프로세스 조회 결과 없음 |
| `git diff --check`·신규 파일 whitespace 검사 | 오류 없음 |

새 검증은 양쪽 대량 출력·40MiB씩 출력 중 256KiB tail 유지, Full 정확히 32MiB/+1byte, shell 문자가 포함된 인자 보존, empty/Unicode/quote 인자, working directory, 0/7 exit code, missing/invalid 실행 파일, callback 예외·root/EOF 정리, UTF-8 read 경계·불완전 종료·BOM byte 보존, ring wrap·큰 단일 chunk·snapshot 독립성, 시작 전 취소를 포함한다.

기능적 보관 상한을 실제 bytes로 검증했으며 OS process working set의 엄밀한 상한 측정은 하지 않았다. OS 읽기 오류 주입·실행 중 취소/timeout의 자식 수명·경합은 이번 검증 결과에 포함하지 않는다. 실제 yt-dlp/FFmpeg·사이트 명령·WPF 실행 회귀도 수행하지 않았다.

## 커밋 지점

권고 메시지: `feat: Add bounded process execution and output capture`.

후보: ProcessExecution 신규 4개 파일, Infrastructure csproj의 테스트 내부 접근 설정, 기존 테스트 csproj의 fixture 연결, fixture csproj/Program.cs, 신규 테스트 2개, 본 결과·상세 설계·Baseline 상태 기록이다. 기존 파일/도구 탐색·패키지 버전·WPF 코드는 바꾸지 않았다. 여기서 멈추며 실제 커밋은 아직 없다.

다음 단위는 **2b 취소·타임아웃·프로세스/자식 수명**이다. E3 재현·증거로 최소 보완을 선택한다. 전체 설계의 시간축·FPS/포맷·사이트 실험 의존 사항과 Phase 1 Completion/tag는 미확정으로 유지한다.
