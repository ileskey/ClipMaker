# Phase 1 4a-1 exact signed 시간 기본형 구현·검증

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `21eee0281f4e188a6e48754044450d1f6cb4f768`, branch `phase/1`, 작업 트리 clean. Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-1 구현·관련 검증 완료**. [G5b API/단위 계획](PHASE_1_G5B_API_READINESS.md)의 첫 단위를 수행했다. source 획득·치지직 adapter·미디어 검사/처리·4단계 전체·전체 지원/Phase 완료는 아니다.

## 변경

- Media.Core `SignedMediaTime`: BigInteger 기약 signed seconds, positive denominator·zero0/1, exact +/-·비교·값 동등성, 검증된 Timecode 성분의 exact bridge. TimeSpan 중간 반올림·표준 FPS 추정 없음.
- `NativeTimestamp`: signed Int64 PTS·positive rational time_base·정확한 PresentationTime. 정수 PTS0은 실제 관찰값이며 missing을0으로 변환하는 기능은 없다. 같은 presentation이라도 다른 native PTS/clock 관찰의 identity는 유지한다.
- `PresentationRange`: signed half-open `[Start,End)`, End>Start, exact membership. 사용자 입력 MediaTimeRange/Timecode의 nonnegative 의미는 그대로다.
- NativeTimestamp.cs의 `NativeTimestampRounding`: Exact/Floor/Ceiling. SignedMediaTime의 native 변환은 fractional Exact를 OutOfRange, 변환된 정수가 Int64 밖이면 Overflow로 반환한다. signed floor/ceil은0방향 정수 나눗셈과 구별한다. 명시한 floor/ceil의 결과가 범위 안이면 허용하며 이전 TimeSpan 경계 정책을 변경하지 않는다.

제품 타입3파일·기존 Core.Tests의 테스트3파일을 추가했다. 기존 프로젝트·패키지·Timecode/FrameRate/MediaTimeRange·Application 오류·도구 정책을 수정하지 않았다. acquisition/probe/service skeleton·미디어 실행을 같은 커밋에 넣지 않았다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipMaker.Media.Core.Tests/ClipMaker.Media.Core.Tests.csproj --no-restore` | exit0, 통과121/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0. 기존 WPF 프로젝트 포함 빌드 |

테스트는 canonical equality/hash·positive denominator·signed arithmetic·큰 정수 비교·native clock의 두 성분·Int64 양끝/rounding overflow·fractional Exact 실패·half-open tiny interval을 검사했다. `29.97`과 `30000/1001`의 구별, Timecode→TimeSpan에서 잃는 정밀도, G2의 두 source/local 좌표 및 G3c의 native ceil/계산된 render residual을 함께 검증했다. 기존 Core 시간 테스트도 포함했다.

이 숫자는 이번 Core 테스트 실행 결과다. 다른 테스트 프로젝트의 전체451개 과거 결과를 재실행 결과로 보고하지 않는다. WPF 빌드 성공을 실제 UI 동작 회귀 검증으로 설명하지 않는다. 새 실험 프로젝트/패키지 설치·원격 조회/다운로드·영상 생성·FFmpeg/ffprobe 실행은 하지 않았다.

## 남은 작업·커밋 지점

다음은 **4a-2 공유 결과/관찰/native 모델·selected inspection/acquisition 계약**이다. 공용 기본형은 구현 완료지만 source/native proof·HTTP 운영 세부·decoder preparation과 이후 미디어 전략은 [G5b](PHASE_1_G5B_API_READINESS.md)의 남은 gate를 그대로 따른다. 치지직 해결책의 제품 adapter 반영은 이후 획득/검사/처리 단위에서 진행한다.

이번 논리적 커밋은 제품3파일·테스트3파일·결과/설계/source 계약/baseline4문서다. Git diff 공백·신규 파일 공백·문서 링크와 변경 범위를 확인했다. staging/commit은 수행하지 않았으며 사용자 파일/기존 실험 자료를 보존했다. 권장 메시지: `feat: Add exact signed media time primitives`.
