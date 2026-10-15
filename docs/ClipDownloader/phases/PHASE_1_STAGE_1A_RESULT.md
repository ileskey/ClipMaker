# Phase 1 / 1단계 첫 커밋 단위 — FPS·타임코드

수행일: 2026-10-10 (Asia/Seoul).

상태: **1a 구현·관련 자동 검증 완료, 커밋 대기**. 1단계 전체·Phase 1 전체 완료가 아니다. 사용자가 현재 branch를 확인하고 1단계를 시작하되 논리적 커밋 시점에 끊어 보고하도록 요청했다. 실험에 독립적인 시간 모델을 구현했으며 미확정 시간축·포맷·사이트·자식 프로세스 전략을 확정하지 않았다.

## 기준·범위

- 실제 branch: `phase/1` (`/phase/1`은 사용자 표현이며 실제 Git 이름은 앞의 값이다).
- 시작 HEAD: `0759dc52475538df848f765ce30c005cf755107f`.
- 기존 미커밋·미추적 문서는 보존했다. commit/staging/branch/tag 변경은 수행하지 않았다.
- 변경: Media.Core의 ValueObjects/FrameRate.cs, TimecodeInput.cs, Timecode.cs, TimeValidationError.cs; Media.Core.Tests의 csproj·FrameRateTests.cs·TimecodeTests.cs; 솔루션에 해당 테스트 등록.
- 테스트 패키지는 Phase 0의 xunit.v3.mtp-off 4.0.1, runner 4.0.0, Test SDK 18.10.1을 재사용했다. 제품 신규 패키지는 없다.

## 실제 구현

- 불변 FPS 분자/분모는 long 양수·약분 유리수다. 소수 FPS를 표준 FPS로 추정하지 않는다.
- TimecodeInput은 HH:MM:SS:FF 문법과 long 저장 범위를 검사하고 FPS 없이 성분을 보존한다. MM/SS 범위는 semantic 검사다. 앞뒤 공백은 허용하지만 내부 공백·Unicode 숫자·부호·소수점·생략형은 거부한다.
- Timecode는 실제 FPS로 MM/SS/FF를 검증하고 생성한다. BigInteger 중간 연산으로 정확한 시간값을 보존한다. TimeSpan 경계에서만 최근접 짝수 tick 반올림을 적용한다. 정확한 값이 TimeSpan 최대값을 넘으면 반올림으로 숨기지 않고 실패한다.
- 연속 프레임 번호는 정확한 시간×FPS의 floor다. 시간코드 표시와 연속 프레임 번호를 동일시하지 않는다. 프레임 번호 long overflow는 별도 실패다.
- Timecode끼리 및 TimeSpan 길이와의 정확한 비교를 제공한다. 같은 tick으로 반올림된 다른 값도 구분한다. 이는 다음 구간/길이 검증 단위의 기반이며 구간 모델 자체는 아직 없다.
- 오류는 syntax/range/overflow와 필드·안전한 설명으로 반환한다. 프로그래밍상의 null 모델 인자는 예외로 처리한다. 실제 FPS 측정·Application 입력 오류 매핑·CLI는 구현하지 않았다.

## 실제 검증

| 명령 | 결과 |
|---|---|
| `dotnet build ClipMaker.slnx --verbosity minimal` (sandbox) | 종료 코드 1, 빌드하지 못했다는 출력만 있고 원인 진단 없음 |
| 같은 build 명령 (승인된 제한 밖 실행) | 종료 코드 0, 경고 0·오류 0; 테스트 프로젝트 복원 성공 |
| `dotnet test ClipMaker.slnx --no-build --verbosity minimal` (sandbox) | 종료 코드 1, 출력 없음 |
| 같은 test 명령 (승인된 제한 밖 실행) | 종료 코드 0; 새 Core 테스트 61 통과·0 실패·0 건너뜀, 기존 탐색 테스트 22 통과·0 실패·0 건너뜀 |
| `git diff --check` | 오류 없음; 미추적 신규 파일은 별도 whitespace 검사 |

테스트에는 소수 FPS, 120fps의 3자리 FF, 100시간 입력, ASCII 문법, syntax/semantic 분리, half-tick 짝수 반올림, 정확한 비교, TimeSpan 최대값·초과, 중간 연산 및 프레임 번호 overflow, FPS 변경 시 재검증이 포함된다. sandbox 실패 원인을 단정하지 않으며 승인된 실행 결과만 통과 근거로 사용한다.

## 도구 확인과 미완료 항목

직전 사용자 요청에서 winget 설치를 확인했다. `winget list --id yt-dlp.yt-dlp --exact --disable-interactivity`는 설치 버전 2026.08.19를 보고했고, 다음 절대경로의 `--version`·`--ignore-config --help` 실행이 종료 코드 0이었다.

`C:/Users/shiro/AppData/Local/Microsoft/WinGet/Packages/yt-dlp.yt-dlp_Microsoft.Winget.Source_8wekyb3d8bbwe/yt-dlp.exe`

현재 실행 셸의 Get-Command에는 새 yt-dlp가 없어서 설치 절대경로를 사용했다. PATH를 수정하거나 도구를 재설치하지 않았다. JS runtime/EJS·사이트 조회·다운로드 성공을 의미하지 않는다. 도구 확인 직후 사용자가 턴을 중단했고 제품 코드 변경 없이 다시 branch 확인부터 이어서 진행했다.

다음 논리적 단위는 **1b MediaTimeRange·구간 검증**이다. 같은 FPS, [Start, End), Start>=0, End>Start, 영상 길이와의 정확한 비교·필드별 오류를 구현·검증한다. 프로세스/다운로드/CLI·Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다. 이후 실험 의존 항목은 기존 계약·실험 준비 문서를 따른다.

권고 커밋 메시지: `feat: add rational FPS and timecode validation`.

커밋 후보는 이번 Media.Core ValueObjects 4개 파일, Media.Core.Tests 3개 파일, ClipMaker.slnx, 본 결과 기록이다. 기존 사용자 문서 전체를 일괄 staging하지 않는다. 상세 설계·Baseline의 이번 기록 반영분은 기존 미추적 자료와 함께 있어 사용자가 문서 커밋 범위를 선택해야 한다. 실제 커밋은 아직 없다.
