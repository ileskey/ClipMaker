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
| Phase 1 | In Progress (1~3단계 선택 구현·자동 검증 완료, CHZZK coverage/후보 trim·원점 반례 확인, playlist/fragment 대조 완료, player 간접 대조 완료, source 시간 하위 계약안 작성, 선택 포맷 시간 의미 채택·G1 제한 좌표 대조 완료, G2 한 source·두 포맷 획득/mapping 대조 완료, G3a 값 차이 대조 완료, G3b 끝/gap 후보 대조 완료, G3c 비정렬/A/V 대조 완료, 새 경계/보정 계약 사용자 채택 완료, G4a Fast baseline·시간축/끝 반례 확인, G4a-2 제한된 보완 후보·MP4 edit 끝 검증 완료, G4b-1 VP9/로컬 분리 진단·Opus sample 제거 확인, G4b-1-2 native Opus 보존 후보 제한 검증 완료, G4b-2a 실제 137/140 개별 원점/coverage 대조 완료, G4b-2b 공통 presentation/movie 변환 제한 대조 완료, G4c-1 CFR/VFR·movie quantization 제한 대조 완료, G4c-2 codec capability/보존 반례·거절 경계 대조 완료, G5a 최소 descriptor/gate/오류 통합안 작성·문서 검산 완료, G5b API/전략 경계·단위별 readiness 정리 완료, 4a-1 exact signed 기본형 구현·검증 완료, 4a-2a 공유 결과/scope/native 모델 구현·검증 완료, 4a-2b selected inspection/획득 계약 구현·검증 완료, 4a-3a bounded HTTP reader 구현·검증 완료, 4a-3b private locator/조회 경계 구현·검증 완료, 4a-3c-1 HLS 조회/참조 계획 구현·검증 완료, 4a-3c-2 bounded 참조수집/소유파일 구현·Downloader385개/빌드 검증 완료, 4a-3c-3a 초기 Demux 관찰 파서·Downloader404개/빌드 검증 완료, 4a-3c-3b local probe/byte identity·Downloader416개/빌드·기존파일 CLI 대조 완료, 4a-3c-3c-1 MP4 timing header reader·Downloader432개/빌드 검증 완료, 4a-3c-3c-2 동일파일 track 연결·Downloader452개/빌드·로컬 실제 probe 검증 완료, 4a-3c-3c-3a fragment decode 관찰·Downloader467개/빌드·실제4anchors 검증 완료, 4a-3c-3c-3b-1 codec/audio 관찰·Downloader487개/빌드·실제local 대조 완료, 4a-3c-3c-3b-2 packet/skip 관찰·Downloader507개/빌드·실제local 대조 완료, 4a-3c-4 HLS 초기pipeline·Downloader520개/빌드 검증 완료, 전체 지원/처리 준비 미완료) | `0759dc52475538df848f765ce30c005cf755107f` | — | — | `phase/1` (사용자 선택, 구현 시작 시 확인) | Pending (Phase 전체 필수 검증 미완료) | [Phase 1 상세 설계](phases/PHASE_1_DESIGN.md), [1a 실제 결과](phases/PHASE_1_STAGE_1A_RESULT.md), [1b 실제 결과](phases/PHASE_1_STAGE_1B_RESULT.md), [2a 실제 결과](phases/PHASE_1_STAGE_2A_RESULT.md), [2b 실제 결과](phases/PHASE_1_STAGE_2B_RESULT.md), [2c 실제 결과](phases/PHASE_1_STAGE_2C_RESULT.md), [3a 실제 결과](phases/PHASE_1_STAGE_3A_RESULT.md), [3b 실제 결과](phases/PHASE_1_STAGE_3B_RESULT.md), [3c-1 계약 결과](phases/PHASE_1_STAGE_3C1_RESULT.md), [3c-2 실제 결과](phases/PHASE_1_STAGE_3C2_RESULT.md), [CHZZK 옵션 대조](phases/PHASE_1_CHZZK_HLS_COMPARISON.md), [CHZZK 음성 coverage 진단](phases/PHASE_1_CHZZK_AV_TIMING_DIAGNOSIS.md), [CHZZK 여유 획득·trim](phases/PHASE_1_CHZZK_PREROLL_TRIM_EXPERIMENT.md), [CHZZK 원점 일반화 대조](phases/PHASE_1_CHZZK_GENERALIZATION_EXPERIMENT.md), [CHZZK playlist/fragment 원점 대조](phases/PHASE_1_CHZZK_SOURCE_ORIGIN_EXPERIMENT.md), [CHZZK player 시간 대조](phases/PHASE_1_CHZZK_PLAYER_TIMELINE_EXPERIMENT.md), [source 시간 하위 계약안](phases/PHASE_1_SOURCE_TIMELINE_CONTRACT_DRAFT.md), [G1 원점/seek 대조](phases/PHASE_1_G1_ORIGIN_SEEK_RESULT.md), [G2 실제 획득 coverage](phases/PHASE_1_G2_COVERAGE_RESULT.md), [G3a 음성 값 대조](phases/PHASE_1_G3A_AUDIO_VALUES_RESULT.md), [G3b AAC 유효 끝/불연속 후보](phases/PHASE_1_G3B_AUDIO_ENDS_RESULT.md), [G3c 비정렬/A/V 끝과 계약안](phases/PHASE_1_G3C_AV_BOUNDARIES_RESULT.md), [G4a Fast 시간축/끝 대조](phases/PHASE_1_G4A_FAST_RESULT.md), [G4a-2 공통 Fast/MP4 끝](phases/PHASE_1_G4A2_FAST_RESULT.md), [G4b-1 로컬 YouTube/VP9](phases/PHASE_1_G4B1_YOUTUBE_LOCAL_RESULT.md), [G4b-1-2 native Opus](phases/PHASE_1_G4B12_OPUS_NATIVE_RESULT.md), [G4b-2a YouTube 원본 범위](phases/PHASE_1_G4B2_YOUTUBE_RANGES_RESULT.md), [G4b-2b 공통 presentation/movie](phases/PHASE_1_G4B2B_SHARED_AXIS_RESULT.md), [G4c-1 cadence/movie](phases/PHASE_1_G4C1_CADENCE_RESULT.md), [G4c-2 codec 경계](phases/PHASE_1_G4C2_CODEC_BOUNDARIES_RESULT.md), [G5a descriptor/gate 통합안](phases/PHASE_1_G5A_DESCRIPTOR_GATE_DRAFT.md), [G5b API/readiness](phases/PHASE_1_G5B_API_READINESS.md), [4a-1 기본형 구현](phases/PHASE_1_STAGE_4A1_RESULT.md), [4a-2a 관찰/native 모델](phases/PHASE_1_STAGE_4A2A_RESULT.md), [4a-2b inspection/획득 계약](phases/PHASE_1_STAGE_4A2B_RESULT.md), [4a-3a HTTP 기반](phases/PHASE_1_STAGE_4A3A_HTTP_RESULT.md), [4a-3b private locator](phases/PHASE_1_STAGE_4A3B_LOCATOR_RESULT.md), [4a-3c-1 HLS 계획](phases/PHASE_1_STAGE_4A3C1_HLS_RESULT.md), [4a-3c-2 참조 수집](phases/PHASE_1_STAGE_4A3C2_COLLECTION_RESULT.md), [4a-3c-3a 관찰 파서](phases/PHASE_1_STAGE_4A3C3A_OBSERVATIONS_RESULT.md), [4a-3c-3b local probe](phases/PHASE_1_STAGE_4A3C3B_PROBE_RESULT.md), [4a-3c-3c-1 MP4 timing](phases/PHASE_1_STAGE_4A3C3C1_CONTAINER_RESULT.md), [4a-3c-3c-2 track 연결](phases/PHASE_1_STAGE_4A3C3C2_LINK_RESULT.md), [4a-3c-3c-3a fragment 관찰](phases/PHASE_1_STAGE_4A3C3C3A_FRAGMENT_RESULT.md), [4a-3c-3c-3b-1 codec 관찰](phases/PHASE_1_STAGE_4A3C3C3B1_CODEC_RESULT.md), [4a-3c-3c-3b-2 packet 관찰](phases/PHASE_1_STAGE_4A3C3C3B2_PACKET_RESULT.md), [4a-3c-4 초기pipeline](phases/PHASE_1_STAGE_4A3C4_INITIAL_PIPELINE_RESULT.md) |

## 4. 갱신 시점

이 문서는 다음 시점에 갱신한다.

1. 새 Phase를 시작하여 Base와 Branch가 확정되었을 때
2. 해당 Phase의 구현과 필수 검증이 완료되어 Completion이 확정되었을 때
3. Completion commit에 Phase 완료 Tag를 부여했을 때
4. Phase의 기준 문서나 검증 상태가 변경되었을 때

Phase 진행 중의 모든 commit을 이 문서에 기록하지 않는다. 세부 변경 이력은 Git history를 사용한다.
