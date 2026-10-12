# Phase 1 4a-2a 공유 결과·scope·native 관찰 기본형

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `2387750077829f338c6ebc30113e41429e531960`, branch `phase/1`, 작업 트리 clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-2a 구현·관련 검증 완료**. [G5b](PHASE_1_G5B_API_READINESS.md)의4a-2를 공용 모델과 Downloader 계약으로 나눠 첫 logical commit을 수행했다. **4a-2 전체는 미완료**이며 source inspection/acquisition 계약은 다음4a-2b다. 실제 치지직/YouTube 획득·media 검사/처리·Phase 완료는 아니다.

## 변경과 계약 범위

Media.Core.Models에5파일을 추가했다. 기존 프로젝트/패키지/공유 value object·Application 오류 코드는 그대로다.

- `MediaOperationResult<T>`: Succeeded는Data만, Failed는MediaFailure만, Cancelled는둘 다없음. MediaFailureKind는 기존 G5b의 공용 실패 분류이며 Downloader 결과 타입·Process 객체·raw 출력에 의존하지 않는다. 메시지는 어댑터가 안전하게 구성해야 하며 이 타입이 임의 문자열을 자동 익명화하는 것은 아니다.
- `MediaStreamReference`·`MediaObservationScope`: file ID/stream index/kind·검사 snapshot ID·Summary/Frames/Samples·demux/native 기준·관찰 범위·Complete/Partial을 보존한다. frame/sample은 bounded range·올바른 stream role을 요구한다. summary range는 미확인일 수 있다. 범위 충족 비교는 동일 snapshot/stream/검사 종류/시간 기준과 Complete·필요 창 포함을 모두 요구한다.
- `MediaInspectionFact<T>`: Verified(value/complete scope), Unknown(issue/scope), Contradicted(issue/scope)를 구별한다. Partial을 Verified로 만들 수 없으며 unresolved 값은null이다. known0과 missing을 혼동하지 않는다. **Verified는 생산자의 증거 판정**이며 factory 호출 자체가 실제 측정/검증을 수행하지 않는다. Qualification과 parser의 증거 판정은 후속 구현이다.
- `NativeTimeRelation`: 지정한 observed time_base의 signed presentation에 명시한 constant correction을 더한다. 다른 time_base를 전달하면 거절한다. scoped fact와 함께 사용하며 source B/global origin·고정24samples 정책을 만들지 않는다. relation 계산만으로 범위 밖 대응을 확인했다고 주장하지 않는다.
- `AudioSampleLattice`: native `G+n/R`·positive sample rate·signed exact/ceil/floor index 변환. 실제 first available sample을G로 자동 추정하지 않는다. **valid support·coverage·decoder 준비를 증명하지 않으며** 해당 관찰/판정은 별도다.

generic result/fact는 지정된 payload를 보존한다. fact의 scope와 기본 native 값 타입은 불변이며 producer가 별도 mutable payload를 전달했을 때 그 내부까지 복사하는 generic serializer/deep clone을 추가하지 않았다. 실제 후속 계약은 불변 snapshot 타입을 전달한다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipMaker.Media.Core.Tests/ClipMaker.Media.Core.Tests.csproj --no-restore` | exit0, 통과136/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

기존121개와 새15개 사례를 포함한다. 결과 상태의 배타성·partial/summary 차단·snapshot/file/stream/검사 종류/시간 기준/필요 범위 불일치·미확인/모순을 검증했다. 이전 Opus 실험의 계산 잔여와0 잔여를 구별하고 native 보정 후 source B 재계산·잘못된 clock·fractional native 표현 실패를 대조했다. sample lattice는48k/44.1k·nonzero phase·signed Int64 양끝·G3c ceil 경계를 검증했다.

이 테스트는 모델 의미/수학과 기존 코드 회귀다. 새 source/native 사실을 측정한 미디어 실험이 아니다. 솔루션 빌드에는 WPF가 포함되지만 실제 UI 회귀 검증은 수행하지 않았다. 다른 프로젝트의 모든 테스트/과거451개를 새 실행으로 보고하지 않는다. 새 테스트 프로젝트·패키지 설치·remote/media/tool 실행은 없다.

## 다음 logical commit과 기록

다음 **4a-2b**는 이 모델에 연결하는 Application 선택 snapshot·inspection request/result·source descriptor/acquired snapshot 및 `ISelectedSourceInspectionService`/`IClipDownloadService` 계약이다. 작업 시간 범위가 미정인 FPS 조사와 확정 target acquisition을 분리하고 identity/scope/unknown 상태를 보존한다. ClipJob/Workspace/실제 adapter를 미리 빈 skeleton으로 늘리지 않는다. 구현 전 요청/반환의 최소 필드를 해당 책임에 맞춰 구체화한다.

4a-3 source/HTTP 정책·실제 획득,4b probe,4c qualification/처리는 이후다. AAC decoder preparation·native/end 일반화의 남은 gate는 G5b 그대로다. 치지직 해결의 실제 제품 adapter 반영은 아직 하지 않았다.

이번 커밋 범위는 Core5파일·tests4파일·결과/설계/source 계약/baseline4문서다. 새 파일 공백·문서 링크·변경 scope를 확인했다. staging/commit은 하지 않고 기존 사용자/실험 자료를 보존했다. 권장 메시지: `feat: Add scoped media inspection and native timing models`.
