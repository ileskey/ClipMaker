# Phase 1 / 1단계 두 번째 커밋 단위 — 구간 검증

수행일: 2026-10-10 (Asia/Seoul).

상태: **1b 구현·관련 자동 검증 완료, 커밋 대기**. 1a와 합쳐 1단계의 공유 시간 모델·입력/구간 검증을 완료했다. 실제 FPS 측정·시간축 실험·Application 실행 조정·Phase 1 전체 필수 검증은 완료하지 않았다.

## 시작 상태·커밋 확인

- 실제 branch는 `phase/1`이고 시작 작업 트리는 깨끗했다.
- HEAD `5477f36e9a2aea61c44ebbf78dec6a9c84e09d35`: `feat: add rational FPS and timecode validation`. 1a의 모델·테스트·솔루션 등록·결과 기록 9개 파일을 포함하는 것을 확인했다.
- 이전 `4f574a0ce198d65d1ae9d78b7ef5e65abaeaa0a3`에는 준비 문서가 기록돼 있었다. Phase 1 원래 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.
- 이번 작업은 staging/commit/branch/tag 변경을 하지 않았다. 기존 1a 수행 기록의 커밋 대기 표현은 당시 기록이며 위 실제 commit 확인이 최신 상태다.

## 구현 범위·책임

- 추가 파일: `src/ClipMaker.Media.Core/ValueObjects/MediaTimeRange.cs`, `tests/ClipMaker.Media.Core.Tests/MediaTimeRangeTests.cs`.
- `MediaTimeRange.TryCreate`는 불변 Start/End·같은 약분 FPS·정확한 End>Start를 보장한다. 범위 의미는 [Start, End)다. Start>=0은 이미 검증된 Timecode의 생성 불변조건으로 보장하므로 음수를 만들 수 있는 별도 경로를 추가하지 않았다.
- `TryValidateDuration(TimeSpan, out TimeValidationError?)`는 전달받은 동일 시간 기준의 길이와 exact End를 비교한다. End==Duration은 허용, End>Duration은 End 필드 오류다. 음수 길이는 Duration 필드 오류다. 0 길이는 양수 구간의 End 검사에서 거부된다.
- 길이 조회·누락·단위/원본 시간 기준 해석·검사 호출·Application InvalidRange 매핑은 해당 Application 구현 단계 책임이다. Core가 영상을 조회하거나 누락 길이를 추정하지 않는다.
- 구간 비교 전에 tick으로 반올림하지 않는다. 매우 짧은 양수 구간을 잘못 거부하거나 길이 초과를 반올림으로 숨기지 않는다. 입력을 자동 이월·clamp·FPS 변경하지 않는다.
- 실패는 기존 TimeValidationError의 OutOfRange와 FrameRate/End/Duration 필드로 표현한다. null endpoints는 프로그래밍 오류로 남긴다. 기존 모델·패키지·솔루션 참조는 바꾸지 않았다.

## 실제 검증

| 명령 | 결과 |
|---|---|
| `dotnet build ClipMaker.slnx --no-restore --verbosity minimal` (sandbox) | 종료 코드 1; 빌드 실패 메시지, 원인 진단 없음 |
| 같은 build 명령 (승인된 제한 밖 실행) | 종료 코드 0, 경고 0·오류 0 |
| `dotnet test ClipMaker.slnx --no-build --verbosity minimal` (승인된 제한 밖 실행) | 종료 코드 0; Core 85 통과, 기존 탐색 22 통과; 실패·건너뜀 0 |
| `git diff --check` 및 신규 파일 별도 whitespace 검사 | 오류 없음 |

Core 85개는 기존 1a 61개와 신규 1b 24개다. 신규 검증은 정상·0 시작·장시간·소수 FPS 구간, 비어 있거나 역순인 구간, 다른 FPS 거부·약분된 동등 FPS 허용, exact 값은 다른데 동일 tick으로 반올림되는 짧은 구간, 영상 끝과 동일/초과, 음수 길이, fractional tick 및 TimeSpan.MaxValue 경계를 포함한다. sandbox 실패 원인을 단정하지 않으며 성공 근거는 승인된 실행 결과다.

WPF 실행·사이트 조회/영상 다운로드·프로세스 실험은 수행하지 않았다. 이 단계에서는 솔루션 빌드와 기존 탐색 테스트로 회귀를 확인했으며 Phase 1 최종 WPF 수명 회귀를 대신 완료했다고 표시하지 않는다.

## 커밋 지점·다음 단계

권고 메시지: `feat: add exact media time range validation`.

이번 커밋 후보는 신규 모델·테스트·본 결과 기록, 상세 설계의 상태/실제 수행 갱신, Baseline의 1단계 상태/결과 링크다. 현재 논리적 단위를 구현·검증했으므로 여기서 멈춘다. 커밋은 아직 없다.

다음은 채택한 2단계 ProcessRunner·도구 실행 진단이다. 그 첫 단위는 실행·출력 처리이며 자식/파이프 취소 완료 조건은 E3 실험·제품 검증으로 확인해야 한다. 시간축·포맷·사이트의 실험 의존 계약과 Phase 1 Completion/tag는 여전히 미확정이다. Phase 2·WPF 다운로드 UI·ClipEditor는 범위 밖이다.
