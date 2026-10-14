# ClipDownloader Phase 1 구현 context 인계

작성일: 2026-10-10 (Asia/Seoul).

최신 사용자 재생 확인(2026-10-15): 사용자가 직전 `qnjEE-wkw8k`137/140·30fps·10~40초 Accurate 재생 검증에 “좋아 통과야”라고 보고했다. [사용자 확인 기록](PHASE_1_H264_RECOVERY_POINT_SUPPORT.md#사용자-재생-확인)을 따른다. 아래 이 사례의 직접 player Pending은 보고 전 이력이다. CHZZK·YouTube Fast와 기존 네 final 전체의 재생 확인으로 확대하지 않으며 Phase1 전체 Passed/Completion/Tag는 Pending이다.

최신 움직이는 영상 지원(2026-10-15): [H.264 recovery-point 대응·실제검증](PHASE_1_H264_RECOVERY_POINT_SUPPORT.md)을 우선한다. `qnjEE-wkw8k`137/140·30fps·10~40초Accurate의NativeSyncState를실제non-sync/recovery-K 바이트관계로진단했다. exact SEI/intra·같은파일/packet evidence를공유및획득probe에연결해raw표식을보존했고Fast IDR조건은유지했다. 최종CLI exit0·A/V[10,40)·decode·900frames/896서로다른hash·샘플이미지/정리를확인했다. 중간SourceIndex와초기adapter증거누락실패도기록했다. Core143/Media186/Downloader856·총1185개통과·build경고/오류0. 직접player·Phase1전체완료는Pending이다.

최신 Accurate terminal PCM 지원(2026-10-14): [끝 AAC 준비·실제 검증](PHASE_1_ACCURATE_TERMINAL_PCM_SUPPORT.md)을 우선한다. `wFOjWCLPDDM`137/140·10~40초는증명된끝640 decoded padding을유효placement에서제외하고전체captured PCM을보존해Accurate실제CLI exit0·A/V[10,40)·최종decode·정리를검증했다. 독립full decode·selected slice와준비/render PCM이byte동일하다. Core143/Media168/Downloader855·총1166개통과·build경고/오류0. 아래terminal Accurate 미완료기록은이전이력이다. 직접player·Phase1전체완료는Pending이다.

최신 YouTube30초Fast 지원(2026-10-14): [10~40초 지원·검증](PHASE_1_YOUTUBE_THIRTY_SECOND_SUPPORT.md)을 우선한다. `wFOjWCLPDDM`137/140은whole 참조47.56/약47.57초가 필요하고terminal AAC384 duration/1024 decoded count가 추가차단이었다. exact edit끝/native/packet/frame 조건에서640 decoded padding을 분리관찰해Fast실제CLI exit0·final·정리를 확인했다. 현재Phase1 CLI예산은64MiB/60초이며40초기록은이전이력이다. Core143/Media166/Downloader851·총1160개통과·build경고/오류0. terminal Accurate PCM준비·모든구간 지원·직접player·Phase1전체완료는확정하지 않는다.

최신 영상 decode 시간 정책 보완(2026-10-14): [시간 정책 수정·검증](PHASE_1_PICTURE_DECODE_TIMEOUT_FIX.md)에서 FFmpeg framehash에 잘못 공유한 FFprobe60초 제한을 제거했다. 기존 FFmpeg 고정 제한 없음/취소 지원 정책으로 맞추고 버전15초·FFprobe60초는 유지한다. 실패 항목도 버전/FFprobe/FFmpeg로 구별한다. build 경고/오류0·Downloader850개 통과·같은 CHZZK Fast 실제 CLI exit0·final hash 동일·정리를 확인했다. 사용자 원래 timeout은 직접 재현하지 못했고 정확한 실패 호출도 미확정이다. 직접 player와 전체 Phase 완료는 Pending이다.

최신 CHZZK 예산 보완(2026-10-14): [제한 예산 보완·실제 지원](PHASE_1_CHZZK_FETCH_BUDGET_SUPPORT.md)을 우선한다. `14791589`의 Fast 46:10~46:40은 목표 파일만 기존 바이트 상한을 초과했다. 제한 실험 성공을 근거로 CLI 기본 획득 예산을 64 MiB / 40초로 조정하고 실제 CLI exit0·final·검사·정리를 확인했다. 별도 전체 A/V decode 성공·실험/CLI final hash 동일, Downloader848개 통과·build 경고/오류0. 아래 기존 상한 유지·미지원 설명은 최초 진단 시점의 이력이다. 모든 구간/Accurate 지원이나 Phase 1 전체 완료로 확대하지 않는다.

최신 CHZZK 획득 상한 진단(2026-10-14): [30초 요청 진단](PHASE_1_CHZZK_FETCH_BUDGET_DIAGNOSIS.md)에서 `14791589`의 Fast 46:10~46:40은 실제 whole segment 18개·36초가 필요해 기존 30초 원본 획득 상한을 넘는 것을 확인했다. planner 오류를 구체화하고 같은 실제 CLI의 실패 표시·정리를 검증했다. Downloader848개 통과·build 경고/오류0. 예산 조정과 목표 구간 생성 지원은 아직 미검증이며 이번 변경을 클립 생성 해결로 표시하지 않는다.

현재 상태(2026-10-14): 사용자 후속 지시에 따라 **1~6단계의 제한된 전략·CLI 구현과 자동/실제 통합 검증을 완료했다.** 현재 호스트/실제 CLI/Windows Ctrl+C/전체 회귀·남은 완료 조건은 [6단계 통합 검증](PHASE_1_STAGE_6_VERIFICATION.md), media 지원 범위는 [4단계 통합 검증](PHASE_1_STAGE_4_VERIFICATION.md), workspace·준비/실행·정리는 [5단계 통합 검증](PHASE_1_STAGE_5_VERIFICATION.md)을 우선한다. Core143/Media Infrastructure143/Downloader835개, 총1121개와 솔루션 build 경고/오류0을 확인했다. 실제 CLI 양 사이트/양 모드의 최종 MP4·전체 A/V decode, 다운로드/처리 Ctrl+C130·정리, 기존 WPF 창 생성/정상 종료를 검증했다. 직접 player 영상/음성·동기 확인이 남아 Phase 1 전체 Passed/Completion/Tag는 Pending이며 Phase2로 진입하지 않는다. 아래 최초 준비 설명·이전 미완료 기록은 해당 시점의 이력이다.

최신 AAC 지원 보완(2026-10-14): [실제 지원·검증](PHASE_1_AAC_PRIMING_SUPPORT_VERIFICATION.md)에서 wFOjWCLPDDM의137/140·0~14:24를 Fast/Accurate 모두 실제CLI에서 확정했다. 원본1600 skip·부분frame·provider의whole packet 대응과Fast output을검증했으며독립PCM도일치했다. 최종Core143/Media Infrastructure159/Downloader846·총1148개실패/skip0·build 경고/오류0. 아래이영상미지원설명은이전진단시점의이력이며현재지원은새문서를우선한다. 직접player 확인과Phase1 전체완료는아직남았다.

후속 사용자 검증(2026-10-14): YouTube wFOjWCLPDDM의 AAC1600 skip·부분 frame을 재현했다. [진단·오류 분류 보완](PHASE_1_YOUTUBE_AAC_SKIP_DIAGNOSIS.md)에 source support 잔여를 기록한다. 현재 이 영상은 미지원이며 clip 생성 해결로 표시하지 않는다. 최신 회귀는 Core143/Media Infrastructure147/Downloader837, 총1127개 실패/skip0 및 build 경고/오류0이다. 기존6단계 성공 사례와 일반 정책은 유지한다.

Git 기준 보완: 기록된 `0759dc5`와 실제 branch 선행 Phase 0 commit `1ca2547`은 hash가 다르지만 parent와 source tree가 같다. `0759dc5` 자체는 현재 HEAD의 ancestor가 아니다. 상세 증거는 위4단계 기록에 남겼으며 기준 문서 hash를 임의 교체하거나 Git 이력을 변경하지 않았다.

상태: **구현 준비용 인계 초안**. 일반 세부 설계와 구현·검증 순서는 사용자 채택 완료다. 최소 실험과 하위 계약 통합 정리가 남아 전체 설계는 아직 확정되지 않았다. 이 문서는 구현 완료 기록이나 제품 코드 작성·도구 설치·Git 조작의 실행 지시가 아니다.

후속 실제 상태(2026-10-10): 사용자 단계별 지시로 1~2단계·3a/3b를 구현·검증했다. 메타데이터 조회와 Node/EJS 최소 구성의 실제 증거는 [3b 결과](PHASE_1_STAGE_3B_RESULT.md)에 기록한다. 다음 단위는 3c이며 최초 준비 체크리스트를 그대로 현재 미완료 목록으로 해석하지 않는다. 전체 설계의 실험 의존 항목·Phase 1 완료는 미확정이다.

3c 후속: [3c-1 화질/선택 계약](PHASE_1_STAGE_3C1_RESULT.md)은 구현·검증했다. [실험 계획](PHASE_1_STAGE_3C_EXPERIMENT_PLAN.md)을 구체화했으나 영상 다운로드 제한은 유지한다. Best/codec 실험과 자동 FormatSelector는 아직 남아 있다.

3c-2 후속: 사용자가 구간 다운로드를 허용했다. [실험·자동 선택 구현](PHASE_1_STAGE_3C2_RESULT.md)을 마치고 커밋 지점에서 멈췄다. CHZZK 획득 실패·VP9 MP4 결과 실패·분리 A/V 시간축을 4단계 선행 보완 사항으로 기록했다. Phase 1 전체 검증은 Pending이며 위 제한/미구현 설명은 당시 상태다.

## 1. 인계 목적과 첫 작업

Phase 1 — CLI/Core Prototype을 담당할 context가 이전 대화 없이 기준 문서·채택 결정·남은 선행 작업을 확인할 수 있도록 한다. 상세 정책은 [Phase 1 설계](PHASE_1_DESIGN.md)를 기준으로 하며 이 요약으로 대체하지 않는다.

새 context의 첫 작업은 자료 읽기, 현재 Git·소스 확인, 하위 계약과 실험 의존성 식별이다. 실제 구현 시작 전에는 설계 제6~7절의 최소 실험 계획·판정 기준을 정하고, 필요한 실험 결과를 설계에 반영하여 시작 조건을 확인한다. 일반 정책을 처음부터 다시 제안하거나 이미 채택한 사항에 승인을 반복 요청할 필요는 없다.

## 2. 읽을 자료와 우선순위

1. [Phase 1 상세 설계](PHASE_1_DESIGN.md): 제4.6~4.32절 사용자 채택, 제5절 통합 검토, 제6~7절 미확정·다음 작업.
2. [Phase 운영 규칙](../PHASE_DESIGN.md), [Phase Baseline](../PHASE_BASELINE.md).
3. [Phase 0 완료 인계](PHASE_0_COMPLETION_HANDOFF.md), [Phase 0 실제 검증](PHASE_0_DESIGN.md#17-구현-context의-실제-수행-결과).
4. [전체 구조](../PROJECT_STRUCTURE_DESIGN.md), [Phase 1 준비 체크리스트](PHASE_1_DESIGN_CHECKLIST.md).
5. [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처](../requirements/02_ARCHITECTURE.md), [구현 계획](../requirements/03_IMPLEMENTATION_PLAN.md), [Codex 지침](../requirements/README_FOR_CODEX.md), [저장소 전략](../requirements/README_FOR_REPOSITORY.md).
6. 적용되는 사용자·저장소 지침, ClipMaker.slnx, 실제 apps/src/tests 계약·프로젝트 참조.

Phase 0의 구현 전 인계보다 완료 인계·실제 수행 결과를 우선한다. 초기 전체 구조 문서의 소스 상태 설명은 과거 관찰이다. Phase 1 체크리스트도 설계 시작 전 자료이므로 현재 채택 결정은 상세 설계에서 확인한다. 충돌은 최신 사용자 지시와 기록된 결정 근거를 확인하고 영향 분석 후 해소한다.

## 3. 기준 소스·보존할 변경

인계 작성 시 실제 확인:

- HEAD / Phase 0 Completion / Phase 1 Base: `0759dc52475538df848f765ce30c005cf755107f`.
- branch: `main`. 구현 branch를 선택하거나 새 branch를 생성하지 않았다.
- 기존 수정 파일: `docs/ClipDownloader/PHASE_DESIGN.md`.
- 기존 미추적 파일: `PHASE_BASELINE.md`, `phases/PHASE_0_COMPLETION_HANDOFF.md`, `phases/PHASE_1_DESIGN_CHECKLIST.md`, `phases/PHASE_1_DESIGN.md`.
- 이번 인계 추가: 이 문서. 코드의 미커밋 변경은 확인되지 않았다.

위 docs는 사용자·설계 context의 자료이므로 삭제·초기화하지 않는다. 새 context에서 상태를 다시 확인한다. 이 인계에서는 commit/tag/branch 변경을 하지 않았다. Git 작업은 새 context의 사용자 지시와 권한 범위를 따른다.

Phase 0의 실제 빌드·22개 테스트·WPF 수명 검증은 기존 기록을 확인했으며 이번 인계에서 재수행하지 않았다. Phase 1 코드는 아직 없다.

## 4. 반드시 유지할 채택 결정

| 영역 | 기준 |
|---|---|
| CLI | diagnose/info/clip, 명시 --output, 기본 best/fast, --help |
| 확인 | 메타데이터·작업 요약 후 Y/N, --yes로 대기 생략; 검증 우회 불가 |
| 취소·종료 코드 | 성공 0, 실행 실패 1, 입력 오류 2, 사용자 취소 130; 최종 확정·정리 경합은 설계 제4.13절 |
| 시간 | 시계 시간 + FF/실제 FPS, syntax/semantic 분리, 유리수 FPS, [Start, End), 임의 30fps 대체 금지 |
| URL·정보 | 공식 HTTPS URL만, 정규화, 포맷별 FPS, 누락·비정상 값의 채택 정책 |
| 모델 | 불변 입력·준비·검증된 작업 분리, 공개 모델에 인증값·서명 URL 없음 |
| 준비·실행 | 확인 전 출력/구간 획득 없음, 미확인 항목 표시, 실행 시 필수 재검사 |
| 프로세스 | 동시 stdout/stderr drain, JSON 32MiB, 진단 tail 각 256KiB, 실행 상태·종료 코드 분리 |
| 시간 제한 | 버전/기능 15초, metadata 120초, 로컬 probe 60초, 중단 후 프로세스/스트림 정리 10초 |
| 재시도 | Application 전체 재시도 없음, yt-dlp 해당 단위마다 2회 추가 시도, fragment 건너뛰기 금지 |
| 출력 | 기존 폴더·MP4, 덮어쓰기 금지, 앱 데이터 작업 폴더, 출력 폴더 staging, 검사 후 최종 확정 |
| 계층 | 사이트 획득은 Downloader, 로컬 미디어 처리는 Media, Y/N·표시는 호스트 |
| 호스트 | net10.0 CLI, WPF 직접 참조 없음, Phase 0 DI/로그 패키지 재사용 |

표는 요약이며 상한의 의미·오류 분류·미확인 FPS 준비·정리 실패 처리 등은 상세 설계의 해당 절을 읽는다. 기존 ToolLocator Found는 파일 존재만 뜻한다. WPF 파일 존재 진단을 실행·버전 진단으로 임의 변경하지 않는다.

## 5. 구현 전 남은 작업

후속 준비 자료: [하위 계약 통합·파일별 변경 단위](PHASE_1_CONTRACT_INTEGRATION.md), [최소 실험 명령·입력 사양·판정 절차](PHASE_1_EXPERIMENT_RUNBOOK.md), [현재 시작 조건 점검](PHASE_1_PREIMPLEMENTATION_PLAN.md). 시그니처·계약·명령의 문서 구체화와 실제 실험 검증을 구분한다. yt-dlp 설치는 사용자가 담당하며 실험 의존 사항은 결과 반영 전까지 미확정이다.

| 항목 | 필요한 산출물·차단 범위 |
|---|---|
| 하위 계약 통합 | ProcessRunner·미디어·출력 세션의 시그니처, 결과/오류 매핑, 숫자·출력 바이트 처리, 파일별 변경 목록 |
| 획득 시간축 | 원본/로컬 위치를 확인할 근거와 모델·변환 방법; Accurate 경로 확정에 필요 |
| FPS·CFR/VFR | 실제 포맷·PTS, 누락 FPS 확인 방법과 순서, Accurate 제한·판정 기준 |
| 코덱·인코더 | Fast 복사 가능 조합, Accurate 인코더, Best 정렬 정책 |
| YouTube 의존성 | 실제 yt-dlp 버전·EJS·JS runtime 필요와 최소 설정; 자동 설치는 채택하지 않음 |
| 프로세스 취소 | 자식·파이프 유지 사례, 종료 확인 방식; root 종료만으로 완료 주장 금지 |
| 실제 사이트 입력 | 접근·길이·추출 구간·선택 포맷 확인과 재현 기록 |

시험 URL은 사용자가 제공했다. 아직 접근·내용·도구 동작을 확인하지 않았다.

- CHZZK: https://chzzk.naver.com/video/15430558
- YouTube: https://www.youtube.com/watch?v=sFXPM_sfJNo

실험마다 질문, 최소 범위, 도구·버전, 입력과 저장 위치, 실행 절차, 사전 판정 기준, 결과 기록 형식을 먼저 정한다. 실제 결과 없이 요구한 Accurate 정확도나 사이트 지원을 성공으로 표시하지 않는다. 추측으로 시간축을 0초 기준으로 고정하거나 전체 원본 다운로드·모드/화질 변경 fallback을 넣지 않는다.

## 6. 채택한 구현·검증 6단계

| 순서 | 구현 | 관련 검증 |
|---|---|---|
| 1 | 시간 모델·입력 검증 | 파싱·소수 FPS·구간·반올림·overflow |
| 2 | ProcessRunner·도구 진단 | 동시 출력·상한·실패·타임아웃·자식 취소 |
| 3 | URL·메타데이터·포맷 선택 | URL·JSON fixtures·누락·비정상 값 |
| 4 | 획득·미디어 검사·처리 | 확인된 시간축의 추출·스트림·결과 검사 |
| 5 | 작업 공간·Application 조정 | 준비·실행·실패·취소·충돌·최종 확정 경합 |
| 6 | CLI 연결·통합 | 명령·코드·Y/N·EOF·--yes·표시·로그·수명 |

단계마다 관련 검증을 수행하고 결과를 상세 설계의 실제 수행 결과에 기록한다. 기존 xunit.v3.mtp-off/VSTest 계약과 테스트 패키지 버전을 유지한다. 필요한 실제 로직이 생길 때 프로젝트·폴더·테스트를 추가한다.

### Commit 분할 제안

6단계는 구현·검증 단위다. Commit은 하나의 책임 변경과 관련 테스트를 묶어 더 작게 나누는 안을 설계 context에서 권고했다. 이는 사용자 채택이 확정된 commit 수나 실제 Git 실행 요청이 아니다.

| 단계 | 후보 묶음 |
|---|---|
| 1 | FPS·타임코드 / 구간 검증 |
| 2 | 실행·출력 / 취소·타임아웃 / 실행 진단 |
| 3 | URL / JSON 변환 / 포맷 선택 |
| 4 | 구간 획득 / 미디어 검사 / 미디어 처리 |
| 5 | 파일 수명·확정 / 준비 유스케이스 / 실행 조정 |
| 6 | CLI 호스트·명령 / 확인·진행·취소 / 통합 검증·결과 기록 |

이 표대로 모두 분리하면 17개다. 실제 변경의 결합도에 따라 합쳐 대략 15~17개로 예상한 권고이며 개수 달성이 목표가 아니다. 가능한 한 각 commit은 빌드·관련 테스트 통과 상태로 유지한다. 모델·계약·구현·테스트를 기계적으로 따로 commit하지 않는다. 개별 commit 검증과 실제 사이트 검증을 포함한 단계 완료를 구분한다.

## 7. 완료 기준과 인계 결과

최종 솔루션 빌드·관련 자동 테스트·로컬 도구 통합 검증·기존 도구 탐색 및 WPF 회귀 확인을 수행한다. CHZZK·YouTube에서 각각 실제 10~30초 클립을 생성하고 영상·음성 재생, 모드별 결과, 취소, invalid URL·도구 누락/실행 실패를 검증한다. 필수 사이트 검증을 실행할 수 없으면 Phase 1 완료로 표시하지 않는다.

상세 설계에 실제 명령·종료 코드·환경/도구 버전·선택 포맷·요청/실제 구간·정확도·A/V 동기·제한·실패를 기록한다. 민감값은 보존하지 않는다. Completion/완료 tag를 임의 확정하지 않으며 Phase 2 설계나 WPF 다운로드 UI·ClipEditor로 범위를 확장하지 않는다.

이번 인계 작성에서는 문서·Git 상태만 확인했다. 빌드·테스트·도구 실행·영상 다운로드·프로젝트 생성은 수행하지 않았다.

## 8. 새 context 시작 요청문

```text
이 context를 ClipDownloader Phase 1 구현·검증 담당으로 사용할 예정이다.
먼저 docs/ClipDownloader/phases/PHASE_1_IMPLEMENTATION_HANDOFF.md와
연결된 Phase 1 설계·운영 규칙·Phase 0 완료 인계·요구사항을 읽어줘.

현재 Phase 1은 일반 세부 설계·구현 순서·검증 계획만 채택됐고,
최소 실험과 하위 계약 정리가 남아 전체 설계 확정 전이야.
Git·실제 소스와 시작 조건을 확인하고, 남은 선행 작업과 실험 계획을 정리해줘.
이미 채택한 일반 정책을 처음부터 다시 승인받지는 마.
아직 제품 코드·프로젝트 생성·패키지/도구 설치·영상 다운로드는 하지 마.

기준 commit은 0759dc52475538df848f765ce30c005cf755107f이며
미커밋·미추적 docs와 사용자 변경을 보존해줘.
실험 의존 사항을 추정으로 확정하지 말고, 준비 완료와 구현 완료를 구분해줘.
후속 구현 요청을 받으면 채택한 6단계와 관련 검증 순서로 진행해줘.
Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 마.
```

2026-10-14 사용자 진행 지시: Phase 1 보고는 commit 단위에서 단계 단위로 변경한다. 논리적 단위의 구현·검증·자동 commit은 유지하되 개별 commit 보고/다음 진행 요청 대기를 생략한다. 현재 4단계(4a 획득→4b 검사→4c 처리)를 순서대로 계속 진행하고 완료 시 통합 보고한다. 5단계 자동 진입·Phase2·WPF 다운로드 UI·ClipEditor는 이 지시에 포함되지 않는다. 미확인 관계/decoder 준비를 완료로 바꾸지 않는다.
