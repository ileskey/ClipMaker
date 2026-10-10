# ClipDownloader Phase 1 설계 준비 자료

작성일: 2026-10-07 (Asia/Seoul).

상태: 전체 계획 context에서 준비 자료 통합 완료. Phase 1 상세 설계·구현·검증은 미시작이며, 체크박스는 새 설계 context에서 처리할 항목이다.

## 1. 목적과 시작 조건

Phase 1 — CLI/Core Prototype의 상세 설계를 새 context에서 진행하기 위한 기준, Phase 0 인계, 설계 작업 목록과 검증 준비 항목을 정리한다. API·패키지·CLI 문법·추출 알고리즘을 이 문서에서 확정하지 않는다.

Phase 0은 구현·필수 검증을 완료했으며 현재 기록과 소스 기준점이 일치한다. 다음 Phase 상세 설계의 선행 완료 조건은 충족했다. 새 context에서는 명세 변경과 현재 Git 상태를 다시 확인한다.

| 항목 | 준비 자료 작성 시 확인한 상태 |
|---|---|
| Phase 0 Completion / Phase 1의 인계 기준 | `0759dc52475538df848f765ce30c005cf755107f` |
| 완료 태그 | `phase-0-complete`, 위 commit과 일치 |
| 현재 HEAD / branch | 위 commit / `main` |
| 미커밋 변경 | 기존 상태는 PHASE_BASELINE.md와 PHASE_0_COMPLETION_HANDOFF.md 미추적. 이번 준비 문서 추가 및 운영 문서 갱신이 이후 변경으로 추가됨 |
| 코드·요구사항 차이 | 작성 시 완료 태그와 비교한 src, apps, tests, 솔루션, requirements에 추적 파일 차이 없음 |
| Phase 0 검증 근거 | PHASE_0_DESIGN.md 제17절: 빌드 경고·오류 0, VSTest 22개 통과, 실제 WPF·실패·수명 검증 |

이 context는 Git·관련 소스·검증 기록을 대조했으며 빌드·테스트·앱 실행을 재수행하지 않았다. 기록된 Phase 0 검증과 이번 확인을 구분한다. 실제 도구 설치·네트워크·사이트 동작은 현재 환경에서 확인하지 않았다.

새 Phase의 Base와 작업 branch는 새 context에서 시작 상태를 확인하여 [Phase Baseline](../PHASE_BASELINE.md)에 기록한다. 위 완료 commit이 코드 기준이며, 준비 문서 등 이후 변경도 별도로 식별한다. 이 문서 작성만으로 Phase 1 Baseline 행, Completion, 완료 태그를 만들거나 임의 commit·branch 변경을 수행하지 않는다.

## 2. 새 context에서 읽을 자료

1. 이 문서: 준비 사항과 상세 설계 작업 목록.
2. [Phase 운영 규칙](../PHASE_DESIGN.md), [Phase Baseline](../PHASE_BASELINE.md).
3. [Phase 0 완료 인계](PHASE_0_COMPLETION_HANDOFF.md): 완료 결과와 후속 제약의 요약.
4. [Phase 0 상세 설계](PHASE_0_DESIGN.md): 특히 제17절 실제 수행 결과. 초기 설계 기록과 이후 구현 결과를 구분한다.
5. [전체 프로젝트 구조](../PROJECT_STRUCTURE_DESIGN.md).
6. [Codex 지침](../requirements/README_FOR_CODEX.md), [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처 원칙](../requirements/02_ARCHITECTURE.md), [구현 계획](../requirements/03_IMPLEMENTATION_PLAN.md), [저장소 전략](../requirements/README_FOR_REPOSITORY.md).
7. 현재 솔루션, apps/src/tests와 적용되는 저장소 지침. 문서 설명과 실제 계약을 대조한다.

PHASE_0_IMPLEMENTATION_HANDOFF.md는 구현 전 인계 기록이다. 최신 완료 상태는 PHASE_0_COMPLETION_HANDOFF.md와 PHASE_0_DESIGN.md의 실제 결과를 사용한다. 완료 인계 요약을 복제·수정하지 않고 링크로 참조한다.

## 3. Phase 0에서 이어받을 실제 기반

### 프로젝트 경계

- `apps/ClipDownloader`: net10.0-windows WPF, Composition, MVVM, 앱·로그 수명.
- `src/ClipDownloader.Application`: 도구 진단 계약과 Application 결과 모델.
- `src/ClipDownloader.Infrastructure`: 세 도구 진단의 조정과 결과 매핑.
- `src/ClipMaker.Media.Core`: net10.0 프로젝트 경계만 존재. 시간·미디어 모델은 아직 없다.
- `src/ClipMaker.Media.Infrastructure`: 공통 실행 파일 경로 탐색.
- `tests/ClipMaker.Media.Infrastructure.Tests`: 설치 도구·시스템 PATH와 분리한 도구 탐색 테스트.

ViewModel의 Infrastructure 직접 사용은 금지하며, WPF의 구현체 연결은 Composition 책임이다. CLI도 WPF 없이 Application 기능을 호출하는 별도 호스트로 설계한다. CLI·ProcessRunner·yt-dlp 처리·FFmpeg 처리·시간 모델을 이미 존재하는 기능으로 가정하지 않는다.

### 재사용할 계약과 정책

- `IToolLocator.Locate(ToolSearchRequest, CancellationToken)`은 동기 파일 탐색 계약이다. 결과는 Found / Missing / SearchFailed와 선택 경로·출처·검사 내역을 보존한다.
- `IToolDiagnosticsService.DiagnoseAsync(CancellationToken)`은 비동기 Application 진단 계약이다. UI에 Media.Infrastructure 타입을 노출하지 않는다.
- `ToolPathSettings`는 읽기 전용 경로 입력이다. 설정 저장·로드·편집 UI는 없다.
- 탐색 순서는 명시 경로 → 앱 기준 tools → PATH다. 명시 경로 오류·누락에는 자동 대체가 없다. 기본 후보 오류는 기록하고 다음 후보를 확인한다.
- 상대 경로 기준은 AppContext.BaseDirectory다. 기본 앱의 PATH는 시작 시 snapshot이며 재탐색이 실행 중 환경 변경을 자동 반영하지 않는다.
- 탐색 취소는 후보 사이 확인이며 파일 시스템 호출의 즉시 중단을 보장하지 않는다.
- App이 Serilog 로거를 소유한다. 새 탐색 차단·취소 → DI 정리 → 종료 로그 → 로거 정리 순서를 유지한다. 외부 프로세스 수명 관리는 아직 구현되지 않았다.
- 테스트는 VSTest와 `xunit.v3.mtp-off 4.0.1` 기반이다. 기존 패키지 조정 이유와 실제 csproj를 확인하고 불필요한 도구 교체를 하지 않는다.

**Found는 실행 파일의 존재만 의미한다.** 실행 가능·버전·지원 기능·미디어 처리 성공과 구분한다. 발견 이후 파일 삭제·변경과 실행 실패도 Phase 1에서 처리해야 한다.

### Phase 0 제한의 분류

| 항목 | Phase 1에서의 취급 |
|---|---|
| 검증 당시 yt-dlp Missing, FFmpeg/ffprobe PATH Found | 과거 관찰이다. Phase 1 구현·실제 검증 시 재확인하고 실행·버전을 기록 |
| ProcessRunner와 외부 프로세스 취소 미구현 | Phase 1의 신규 설계·구현·검증 대상 |
| 설정 저장·로드·편집 UI 없음 | 자동 추가하지 않음. CLI 입력 전달의 최소 요구만 검토 |
| Test Explorer·실제 날짜 경계 로그 전환 미실행 | Phase 0 비차단 제한으로 보존. Phase 1 선행 작업으로 확장하지 않음 |
| 로그 보관 최근 파일 14개, 기록 경계에서 크기 분할 | 기존 정책을 정확히 인계. 14일 보관·엄격한 10MiB 상한으로 해석하지 않음 |
| 임시 검증 하네스 정리됨 | 상세 결과는 재사용하되 해당 파일이 남아 있다고 가정하지 않음 |

## 4. Phase 1 범위와 제외 범위

명세상 목표는 URL, Start, End, Output path를 입력받아 MP4 클립을 생성하는 CLI/Core prototype이다.

필수 작업 축:

- Timecode 파싱·검증·시간 변환.
- 공통 ProcessRunner: 비동기 stdout/stderr 처리, 종료 코드, 취소와 정리.
- CHZZK·YouTube URL 판별, yt-dlp 메타데이터 JSON과 모델 변환.
- 필요한 구간 획득과 FFmpeg 기반 MP4 생성.
- CLI 호스트 및 오류·진행률·취소 전달.
- 실제 사이트별 짧은 클립과 관련 단위·통합 검증.

전체 제품의 Fast/Accurate와 화질 선택을 이번 prototype에서 어디까지 제공할지 상세 설계에서 명시한다. 기본값·제한과 후속 구현 항목을 기록하고 요구사항을 임의로 제거하지 않는다.

제외: WPF 다운로드 UI, 메타데이터 상세 화면, 여러 구간 큐, Preview, 브라우저 연동, 로컬 영상 사용자 기능, ClipEditor, 배포·자동 도구 업데이트·클라우드 의존성. 공통 미디어 처리의 로컬 입력 경계는 고려하되 로컬 파일 제품 기능을 선행 구현하지 않는다.

## 5. 상세 설계 전 준비 체크리스트

- [ ] Git HEAD·태그·branch·미커밋 파일을 다시 확인하고 docs를 보존한다.
- [ ] Phase 0 완료 기록과 실제 계약을 읽고 시작 조건을 재확인한다.
- [ ] 사용자에게서 발생한 명세 변경과 기존 문서 간 충돌을 확인한다. 영향이 있으면 변경 처리 규칙에 따라 선행 보완을 판단한다.
- [ ] Phase 1 Base·branch와 관련 문서를 식별하여 Baseline에 반영한다. Git 조작은 별도 요청 없이 하지 않는다.
- [ ] 설계에서 결정할 항목과 실제 도구·네트워크 검증이 필요한 가정을 분리한다.
- [ ] 테스트용 영상 확보 방식, 임시 파일·출력 위치, 결과 기록 항목을 준비한다.

현재 두 플랫폼의 실제 검증 URL은 이 준비 자료에 제공되지 않았다. 새 context에서 사용자가 다운로드·편집 권한을 가진 접근 가능한 URL을 받거나 확보 방법을 정한다. URL 부재는 일반 상세 설계의 시작을 막지 않지만, 실제 사이트 완료 검증 전에는 반드시 해결해야 한다. 실제 URL이나 성공 결과를 만들어내지 않는다.

도구 설치·다운로드·실행·사이트 spike를 이번 준비 작업에서 수행하지 않았다. 설계 단계에서 실험이 필요하면 질문, 최소 범위, 산출물과 실행 권한을 먼저 구분하고 현재 요청 범위 안에서 진행한다. 실험 코드가 제품 기능이나 구현 승인으로 확대되지 않도록 한다.

## 6. 새 context에서 상세 설계할 목록

### A. 호스트와 계층

- [ ] CLI 위치·TFM·참조 관계와 DI·logging의 구성 책임을 정한다.
- [ ] WPF Composition 코드를 CLI에서 직접 참조하지 않고 필요한 기반을 연결하는 방식을 정한다.
- [ ] 단일 클립 유스케이스, 사이트 어댑터, 공유 미디어 처리의 책임과 데이터 흐름을 정한다.
- [ ] 추가 모델·계약·프로젝트·파일을 실제 Phase 1 필요에 맞춰 최소화한다.

### B. 시간·구간 모델

- [ ] 유리수 FPS, HH:MM:SS:FF 파싱, 허용 범위, TimeSpan·프레임 변환과 반올림 정책을 정한다.
- [ ] 전체 구조 설계의 시계 시간 + 프레임 오프셋 정책을 검토하고 SMPTE non-drop/drop-frame과 혼용하지 않도록 명시한다.
- [ ] 선택 포맷별 FPS, FPS 누락·소수 FPS·VFR의 처리를 정한다. 임의 30fps를 적용하지 않는다.
- [ ] Start >= 0, End > Start, End <= Duration 및 종료 경계 의미를 같은 시간 기준으로 정한다.

### C. 프로세스 실행과 도구 진단

- [ ] 인자를 개별 값으로 전달하고, stdout/stderr 동시 읽기·출력량·종료 코드·실패 정보를 다루는 계약을 정한다.
- [ ] 취소·실행 실패·정상 종료를 구분하고 자식 프로세스 종료, 비동기 대기와 스트림·자원 정리 방식을 정한다.
- [ ] 경로 발견 이후 실행 가능·버전·필요 기능 확인의 시점과 오류 처리를 정한다.
- [ ] CLI Ctrl+C와 프로세스 수명을 연결한다. WPF 도구 탐색 취소 검증을 외부 프로세스 검증으로 대신하지 않는다.
- [ ] 민감한 URL·쿠키·인증값을 로그에서 보호하며 사용자 오류와 원시 진단 기록을 분리한다.

### D. 소스·메타데이터·포맷

- [ ] 공식 지원 URL의 호스트·경로·영상 ID와 미지원 URL 처리 기준을 정한다.
- [ ] machine-readable JSON 조회·파싱, 제목·길이·FPS·포맷·업로더의 누락 처리와 fixture 범위를 정한다.
- [ ] 분리된 video/audio 스트림과 해상도·FPS·코덱 선택을 표현한다.
- [ ] 인증 필요·사용 불가 콘텐츠·라이브·재생목록 등 MVP 범위 밖 입력을 어떻게 구분할지 정한다. 인증 UX를 근거 없이 추가하지 않는다.

### E. 획득·클립 처리·출력

- [ ] 원격 구간 획득과 공유 FFmpeg 처리의 경계, 실제 사용 전략과 제한을 정한다.
- [ ] 획득 파일의 원본/로컬 시간축을 확인할 방법을 정한다. 요청 시작을 임시 파일 0초로 가정하지 않는다.
- [ ] Fast 키프레임 오차와 Accurate 정확도 확인 기준을 정한다. VFR·누락 정보로 정확도를 보장할 수 없는 경우를 명시한다.
- [ ] MP4 코덱 호환성, mux·remux·재인코딩과 중복 처리 방지를 정한다.
- [ ] 실패 시 전체 원본 다운로드나 모드 변경을 자동으로 수행하지 않도록 fallback 정책을 명시한다.
- [ ] 출력 이름·경로·충돌·작업별 임시 폴더·최종 결과 확정·실패/취소 정리와 정리 실패 로그를 정한다.
- [ ] 다운로드/처리 단계 진행률과 알 수 없는 진행률의 표시를 정한다.

### F. CLI와 검증

- [ ] 필수 입력, 기본값, 화질·모드·도구 경로의 최소 옵션, 출력 메시지·종료 코드를 정한다.
- [ ] Timecode·URL·파일명·JSON 파싱·유스케이스 오류/취소의 단위 검증을 정한다.
- [ ] 실제 프로세스의 출력량·stderr·비정상 종료·자식 프로세스 취소와 정리 검증을 정한다.
- [ ] 설치 도구와 네트워크에 독립적인 테스트, FFmpeg 통합 검증, 실제 사이트 검증을 구분한다.
- [ ] Phase 0 탐색 테스트와 WPF 기본 동작을 보존할 회귀 확인 범위를 정한다.
- [ ] 단계별 구현 순서, 필요한 패키지의 공식 근거·환경 호환성, 필수 완료 조건과 기록 형식을 정한다.

## 7. 완료 검증 준비와 미확정 입력

| 검증 대상 | 상세 설계에서 준비할 내용 | 현재 상태 |
|---|---|---|
| CHZZK VOD / YouTube | 각 사이트의 접근 가능한 영상, 10~30초 길이의 추출 구간 | URL·구간 미정 |
| MP4 결과 | 영상·음성 재생, 선택 포맷, 실제 길이·시작 위치·A/V 동기 확인 방법 | 방법·허용 오차를 설계에서 확정 |
| Fast / Accurate | 모드별 정확도 주장과 검증 기준 | 실제 시간축 검증 필요 |
| 취소 | 다운로드·처리 중 취소, 프로세스 종료와 부분 파일 처리 | 설계·구현·검증 대상 |
| 오류 | invalid/unsupported URL, yt-dlp·FFmpeg 누락, 실행 실패 | 필수 검증으로 연결 |
| ffprobe | 결과 검사 등에 필요한 범위와 누락 시 정책 | 상세 설계에서 결정 |
| 실제 도구 | 버전·기능·실행 환경, 획득/설치 경로 | 현재 미확인, 구현 검증 전 준비 |

완료 기록에는 기준 소스, OS·SDK·도구 버전, 영상 식별·선택 포맷, 요청 구간·실제 결과, 명령·종료 코드·로그와 실패/미검증 사유를 포함한다. 인증 토큰·쿠키·서명된 스트림 URL은 기록이나 fixture에 보존하지 않는다.

필수 사이트 검증을 실행하지 못하면 로컬 테스트가 통과해도 Phase 1 완료로 선언하지 않는다. 미확정 입력과 실험 가정은 상세 설계 문서에 해결 시점·차단 범위를 기록한다.

## 8. 설계 context의 산출물과 다음 인계

새 context는 `docs/ClipDownloader/phases/PHASE_1_DESIGN.md`를 작성한다. 상태를 설계 중으로 시작하고 다음 내용을 포함한다.

1. 기준 문서·소스 상태, 목표·범위·제외 범위.
2. 프로젝트·파일 변경 계획과 참조 관계.
3. CLI 입력에서 클립 완료까지의 책임·계약·상태·데이터 흐름.
4. 시간/FPS, 프로세스, 메타데이터, 추출·출력·오류/취소의 상세 정책.
5. 선택 이유·대안·공식 근거와 검증해야 하는 가정.
6. 구현 순서·완료 조건별 자동/실제 검증 계획.
7. 미해결 사항·필수 검증 입력·구현 context 인계와 Phase 2 연결 제약.

설계와 구현 context를 다시 나누는 경우 확정 설계와 구현 인계 자료를 전달한다. 설계 확정이 Phase 1 구현·검증 완료를 뜻하지 않는다. Phase 2 상세 설계는 Phase 1 구현·필수 검증 완료 후에만 시작한다.

## 9. 새 context 시작용 요청문

```text
ClipDownloader Phase 1 — CLI/Core Prototype의 상세 설계만 진행해줘.
아직 코드를 작성하거나 프로젝트 생성·패키지 설치·영상 다운로드는 하지 마.

docs/ClipDownloader/phases/PHASE_1_DESIGN_CHECKLIST.md를 먼저 읽고,
연결된 Phase 0 완료 인계·실제 검증 결과·Phase 운영 규칙·Baseline,
전체 구조·요구사항과 현재 소스를 확인해줘.

Phase 0 Completion은 0759dc52475538df848f765ce30c005cf755107f이고
phase-0-complete 태그가 같은 commit을 가리켜. 현재 Git 상태를 재확인하고
미추적 docs와 사용자 변경을 보존해줘. Phase 1 Base와 branch를 문서로 식별하되
commit·tag·branch 변경은 별도 요청 없이 수행하지 마.

준비 자료의 쟁점을 기술적 근거와 trade-off로 검토해서
docs/ClipDownloader/phases/PHASE_1_DESIGN.md에 상세 설계를 작성해줘.
합의된 Phase 0 경계와 정책을 유지하고, 바꿀 필요가 있으면 근거와 영향을 기록해줘.
실제 도구·사이트 검증이 필요한 가정과 설계 결정을 구분하고,
미제공 테스트 URL과 도구 준비의 해결 시점을 정해줘.

Phase 2 상세 설계·다운로드 WPF UI·ClipEditor는 진행하지 마.
필요한 실험은 질문·범위·산출물부터 제시하고 현재 설계 요청 범위와 구분해줘.
```
