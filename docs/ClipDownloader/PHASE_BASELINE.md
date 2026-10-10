# ClipDownloader Phase Baseline

## 1. 목적

이 문서는 ClipDownloader의 각 Phase가 Git상에서 어디서 시작했고 어디서 완료되었는지, 그리고 해당 완료 상태가 필수 검증을 통과했는지를 기록한다.

이 문서는 상세 Git 이력이나 개별 commit의 변경 내용을 복제하지 않는다. 각 Phase의 경계와 기준 소스 상태만 식별한다.

## 2. 기록 원칙

각 Phase에 대해 다음 항목을 기록한다.

| 항목 | 의미 |
|---|---|
| **Phase #** | 어떤 Phase인지 식별한다. |
| **상태** | 해당 Phase가 진행 중인지 완료되었는지 기록한다. |
| **Base** | 해당 Phase가 시작된 Git 기준점의 **commit hash**를 기록한다. |
| **Completion** | 해당 Phase의 구현과 필수 검증이 완료된 Git 기준점의 **commit hash**를 기록한다. |
| **Tag** | Completion commit에 부여한 사람이 읽기 쉬운 Git tag를 기록한다. |
| **Branch** | 해당 Phase를 작업한 Git branch를 기록한다. |
| **검증** | Completion 상태가 해당 Phase의 완료 조건을 통과했는지 기록한다. |
| **관련문서** | 해당 Phase의 상세 설계, 구현 결과, 검증 근거를 확인할 수 있는 문서를 기록한다. |

### Base

`Base`는 해당 Phase를 시작할 때 기준으로 삼은 commit이다.

예를 들어 Phase 1이 Phase 0 완료 상태에서 시작되었다면, Phase 1의 Base에는 Phase 0 완료 시점의 commit hash를 기록한다.

Tag가 존재하더라도 Base에는 Tag 이름이 아니라 실제 commit hash를 기록한다.

### Completion

`Completion`은 해당 Phase의 구현뿐 아니라 필수 검증과 결과 기록까지 완료되어, 해당 Phase의 결과물로 확정한 commit이다.

구현만 끝났거나 필수 검증이 남아 있다면 Completion을 확정하지 않는다.

Tag가 존재하더라도 Completion에는 Tag 이름이 아니라 실제 commit hash를 기록한다.

### Tag

`Tag`는 Completion commit에 붙이는 사람이 읽기 쉬운 식별자다.

예:

```text
phase-0-complete
phase-1-complete
```

Tag는 Phase 완료점을 쉽게 찾기 위한 이름이며, Base와 Completion의 commit hash를 대체하지 않는다.

### 상태

상태는 최소한 다음 두 값을 사용한다.

- `In Progress`: 해당 Phase가 아직 완료 조건을 충족하지 않은 상태
- `Completed`: 구현과 필수 검증을 완료하고 Completion 기준점이 확정된 상태

필요한 경우 프로젝트 운영 규칙에 따라 상태를 추가할 수 있다.

### 검증

검증은 Completion이 해당 Phase의 완료 조건을 만족하는지 나타낸다.

기본적으로 다음 값을 사용한다.

- `Pending`: 아직 필수 검증이 완료되지 않음
- `Passed`: 필수 검증을 모두 통과함

필수 검증이 남아 있다면 Phase 상태를 `Completed`로 기록하지 않는다.

상세 테스트 명령, 실행 결과, 수동 검증 내용, 실패 원인 등은 이 문서에 중복 기록하지 않고 관련 Phase 문서에서 관리한다.

## 3. Phase Baseline 기록

| Phase # | 상태 | Base | Completion | Tag | Branch | 검증 | 관련문서 |
|---|---|---|---|---|---|---|---|
| Phase 0 | Completed | `fc96883b18c1d6856c92ad6b029d45e095e41e69` | `0759dc52475538df848f765ce30c005cf755107f` | `phase-0-complete` | `main` | Passed | [Phase 0 상세 설계·구현 및 검증 결과](phases/PHASE_0_DESIGN.md) |
| Phase 1 | In Progress (1~2단계·3a 검증 완료, 3b/3c·미디어/사이트 실험 미완료) | `0759dc52475538df848f765ce30c005cf755107f` | — | — | `phase/1` (사용자 선택, 구현 시작 시 확인) | Pending (Phase 전체 필수 검증 미완료) | [Phase 1 상세 설계](phases/PHASE_1_DESIGN.md), [1a 실제 결과](phases/PHASE_1_STAGE_1A_RESULT.md), [1b 실제 결과](phases/PHASE_1_STAGE_1B_RESULT.md), [2a 실제 결과](phases/PHASE_1_STAGE_2A_RESULT.md), [2b 실제 결과](phases/PHASE_1_STAGE_2B_RESULT.md), [2c 실제 결과](phases/PHASE_1_STAGE_2C_RESULT.md), [3a 실제 결과](phases/PHASE_1_STAGE_3A_RESULT.md) |

## 4. 갱신 시점

이 문서는 다음 시점에 갱신한다.

1. 새 Phase를 시작하여 Base와 Branch가 확정되었을 때
2. 해당 Phase의 구현과 필수 검증이 완료되어 Completion이 확정되었을 때
3. Completion commit에 Phase 완료 Tag를 부여했을 때
4. Phase의 기준 문서나 검증 상태가 변경되었을 때

Phase 진행 중의 모든 commit을 이 문서에 기록하지 않는다. 세부 변경 이력은 Git history를 사용한다.
