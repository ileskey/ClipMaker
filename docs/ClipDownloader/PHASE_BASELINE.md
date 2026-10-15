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
| Phase 1 | Completed | `0759dc52475538df848f765ce30c005cf755107f` | `4509f54dadbf140f787bdfd4a16dcc2b1f964c48` | `phase-1-complete` | `phase/1` | Passed (2026-10-15: 양 사이트·양 모드 사용자 확인, 최종 build 및1195개 회귀) | [최종 검증 인계](phases/PHASE_1_FINAL_VERIFICATION_HANDOFF.md), [설계·이력](phases/PHASE_1_DESIGN.md), [6단계](phases/PHASE_1_STAGE_6_VERIFICATION.md) |
| Phase 2 | Completed | `3cbb04ed09df07d75dc5b963905b497909d8daeb` | `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f` | `phase-2-complete` | `phase/2` | Passed (build 경고/오류 0, 1230개 테스트, 양 사이트·양 모드 실제 WPF 생성·사용자 재생/동기 확인) | [상세 설계](phases/PHASE_2_DESIGN.md), [구현·검증 기록](phases/PHASE_2_IMPLEMENTATION.md), [설계 context 인계](phases/PHASE_2_DESIGN_HANDOFF.md) |
| Phase 3 | In Progress | `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712` | — | — | `phase/3` | Pending (최종 실제 UI 재확인·100/200% native DPI 관찰 남음) | [상세 설계](phases/PHASE_3_DESIGN.md), [구현·검증 기록](phases/PHASE_3_IMPLEMENTATION.md) |

2026-10-15 Phase 2 Git 준비 당시: 실제 시작 Base는 `phase/1`을 병합한 `3cbb04e`다. Phase 1 Completion `4509f54`는 이 Base의 ancestor이며 두 commit의 차이는 인계 문서 6개뿐이었다. 준비 시점에는 Phase 2 완료 태그를 생성하지 않았다.

2026-10-15 Phase 2 완료 확정: 기존 `phase-2-complete`의 dereferenced commit이 `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`임을 확인했다. 검증된 제품 소스 `86fd9f8014aba1135fe30ba6d5b14be4df3ab152`는 Completion의 ancestor다. 이전 검증 기록의 커밋과 현재 커밋은 각각 tree가 동일하며, 해시 대응과 실제 검증 범위는 구현·검증 기록을 따른다. 후속 문서 정합성 커밋은 Completion이나 태그를 이동하지 않는다.

2026-10-14 Git 보완: 기록된 Phase 0 Completion/Phase 1 Base `0759dc5`는 현재 HEAD의 ancestor가 아니며 실제 선행 Phase 0 commit은 `1ca2547`이다. 두 commit의 parent와 source tree는 같고 소스 diff는0이다. 기록 hash를 임의 교체하거나 이력을 변경하지 않는다. [4단계 Git 대조](phases/PHASE_1_STAGE_4_VERIFICATION.md)에서 기준 snapshot과 실제 ancestry를 구별한다.

## 4. 갱신 시점

이 문서는 다음 시점에 갱신한다.

1. 새 Phase를 시작하여 Base와 Branch가 확정되었을 때
2. 해당 Phase의 구현과 필수 검증이 완료되어 Completion이 확정되었을 때
3. Completion commit에 Phase 완료 Tag를 부여했을 때
4. Phase의 기준 문서나 검증 상태가 변경되었을 때

Phase 진행 중의 모든 commit을 이 문서에 기록하지 않는다. 세부 변경 이력은 Git history를 사용한다.
