# ClipDownloader Phase 2 설계 context 인계

현재 상태 (2026-10-15): Phase 2는 **Completed / Passed**, Completion `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`, 태그 `phase-2-complete`다. 최종 결과·지원 경계·검증된 제품 기준은 [구현·검증 기록](PHASE_2_IMPLEMENTATION.md)을 따른다. 아래 내용은 Phase 2 설계 시작 전의 역사적 인계이며, 당시 branch/미구현 설명을 현재 상태로 해석하지 않는다.

작성일: 2026-10-15 (Asia/Seoul). 대상은 **Phase2 — WPF MVP 상세 설계**다. Phase1 구현 context에서 완료 결과·실제 계약·제약을 정리한 준비 자료이며 Phase2 상세 설계안의 채택이나 제품 구현 착수 기록이 아니다.

## 1. 기준 소스와 보존 조건

| 항목 | 확인된 값 |
|---|---|
| Phase1 Completion / Phase2 설계 Base | `4509f54dadbf140f787bdfd4a16dcc2b1f964c48` |
| 완료 tag | `phase-1-complete` — 실제 dereferenced commit과 위 hash가 일치 |
| Phase1 상태·검증 | Completed / Passed |
| 인계 작성 시작 HEAD·branch | 위 Completion / `phase/1` |
| 최종 솔루션·회귀 | build 경고/오류0, Core143 / Media Infrastructure193 / Downloader859, 총1195개 실패/skip0 |
| 실제 검증 도구 | yt-dlp2026.08.19 / FFmpeg·FFprobe8.1 / Node22. 도구 경로·실행 가능성은 새 context에서 재확인하고 자동 설치하지 않음 |
| 후속 문서 변경 | Completion 이후의 완료 기록·이 handoff 정리이며 완료 태그를 이동하지 않음 |

새 context는 작업 시점의 HEAD·branch·tag·미커밋/staged/untracked 변경을 다시 확인한다. Phase2 branch는 아직 선택·생성하지 않았다. 현재 `phase/1`에서 바로 제품을 수정하거나 임의로 branch/tag/history를 바꾸지 않는다. 사용자 지시에 따른 작업 방식을 유지한다.

인계 작성 시 미추적 사용자 파일은 `22.mp4`, `out.mp4`, `out2.mp4`, `out4.mp4`, `out5.mp4`, `out6.mp4`다. ignored `.artifacts`에는 실험 원본·검증 영상·안전한 harness가 있다. 사용자 파일과 docs 변경을 보존하고 reset/clean/전체 폴더 삭제를 하지 않는다. 오래된 실험의 signed URL을 재사용하거나 원시 metadata를 문서에 복제하지 않는다.

Phase1 원래 Base `0759dc52475538df848f765ce30c005cf755107f`는 현재 history의 ancestor가 아니다. 실제 선행 `1ca254790cb9b6daa6bd79539a5c41e91f27cad4`와 parent/tree가 같고 diff0임을 확인했지만 원인은 미확정이다. [최종 인계](PHASE_1_FINAL_VERIFICATION_HANDOFF.md)의 설명을 유지하고 원래 Base를 조용히 대체하지 않는다. Phase2는 새로 확인한 실제 Phase1 Completion을 기준으로 삼는다.

## 2. 읽을 자료와 우선순위

1. 현재 사용자 지시와 저장소 AGENTS.md.
2. [Phase 운영 규칙](../PHASE_DESIGN.md), [Baseline](../PHASE_BASELINE.md).
3. [구현 계획·Phase2 완료 조건](../requirements/03_IMPLEMENTATION_PLAN.md), [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처](../requirements/02_ARCHITECTURE.md).
4. [전체 구조](../PROJECT_STRUCTURE_DESIGN.md), [Codex 지침](../requirements/README_FOR_CODEX.md), [저장소 지침](../requirements/README_FOR_REPOSITORY.md).
5. [Phase1 최종 검증 인계](PHASE_1_FINAL_VERIFICATION_HANDOFF.md), [Phase1 상세 설계·채택 이력](PHASE_1_DESIGN.md), [4단계](PHASE_1_STAGE_4_VERIFICATION.md), [5단계](PHASE_1_STAGE_5_VERIFICATION.md), [6단계](PHASE_1_STAGE_6_VERIFICATION.md), [CLI 사용법](../../../apps/ClipDownloader.Cli/README.md).
6. [Phase0 완료 인계](PHASE_0_COMPLETION_HANDOFF.md)와 실제 WPF 소스·Application 계약·CLI composition.

예전 계획의 예시 인터페이스나 초기 상태를 현재 구현으로 추정하지 않는다. 충돌은 최신 사용자 지시·채택 결정과 실제 코드로 판정한다. 이미 채택한 일반 시간/선택/검증 정책을 처음부터 다시 승인받지 않는다. 새 UI 결정과 기존 정책 변경은 구분한다.

## 3. Phase2 목표와 제외 범위

기존 `apps/ClipDownloader` WPF 호스트에 URL 입력·영상 불러오기·Start/End `HH:MM:SS:FF`·Quality·Fast/Accurate·명시 output path/파일 선택·실행·progress/status·취소를 연결하는 MVP를 설계한다. metadata 조회 후 시간/선택/출력 검증과 기존 준비·확인·실행 흐름을 UI에서 제공해야 한다.

필수 완료 조건은 UI freeze 없음, 작업 취소, 실제 CHZZK/YouTube 생성·재생, Start/End 및 output validation이다. 현재 지원하는 입력 범위와 실패 처리를 함께 검증한다. Phase1 성공 기록만으로 새 WPF 통합 검증을 대신하지 않는다.

Phase3의 상세 metadata/thumbnail UI, Phase4 작업 queue·병렬 다운로드, Phase5 내장 preview/player, ClipEditor는 제외한다. Phase2에서 시간과 화질 선택에 필요한 최소 metadata는 사용한다. 새 미디어 전략·원격 사이트 지원 확대·패키지/도구 설치를 UI 연결에 자동 포함하지 않는다.

## 4. 현재 실제 소스와 연결 계약

| 영역 | 실제 소스·의미 |
|---|---|
| WPF 기본 호스트 | `apps/ClipDownloader/App.xaml.cs`, `Composition/ServiceRegistration.cs`, `Views/MainWindow.xaml`, `ViewModels/MainViewModel.cs` |
| 현재 WPF 기능 | Phase0 초기화·파일 존재 기반 도구 탐색/표시. clip 실행·metadata 입력 UI는 아직 없음. 기존 시작/종료·로그를 보존할 대상 |
| CLI composition 참고 | `apps/ClipDownloader.Cli/Composition/ServiceRegistration.cs` — WPF가 CLI 프로세스나 console UI를 호출할 이유는 없음. 서비스 연결과 소유자 수명을 참고 |
| metadata·URL | `IVideoSourceParser`, `IVideoInfoService`, `VideoInfo`, `FormatSelector`·`SelectedSourceSnapshot` |
| 준비/실행 | `IClipJobService.PrepareAsync(ClipRequest, VideoInfo, CancellationToken)` → `OperationResult<PreparedClip>`; `ExecuteAsync(PreparedClip, IProgress<JobProgress>?, CancellationToken)` → `ClipJobResult` |
| 불변 준비 자료 | `PreparedClip`: JobId, 선택snapshot, Start/End, nullable Range, FinalPath, Title/Uploader, Budget, Warnings, PendingChecks |
| 진행·최종 결과 | `JobProgress`의 Stage/nullable Percent/Message와 `ClipJobResult`의 Status/Error/OutputPath/Warnings/RemainingTemporaryPaths/Presentation |
| 검증된 실제 범위 | `ClipPresentationSummary`: 요청·video/audio 표시 범위, SourceEndVerified, zero/overlap/AAC skip/tail counts |
| 시간 기본형 | `src/ClipMaker.Media.Core/ValueObjects`의 TimecodeInput/Timecode/FrameRate와 exact signed/rational 시간. UI double 계산으로 대체하지 않음 |

ViewModel이 yt-dlp/FFmpeg 인자를 만들거나 native/probe를 우회해서 성공을 결정하지 않는다. `Prepare`는 쓰기 없는 확인 자료이고 성공해도 원본/출력 검증이 끝났다는 뜻이 아니다. `Execute`는 재검사·획득·검증·최종 확정·정리를 수행하며 prepared job은 한 번만 실행한다. 사용자 입력/선택/metadata가 바뀌면 이전 준비 결과를 재사용할 수 있는지 UI에서 명시적으로 관리해야 한다.

`SelectedSourceAcquisitionService`는 inspection/download/resource access가 같은 소유자 instance를 사용해야 한다. CLI DI의 source singleton과 media/workspace 수명을 참고한다. WPF composition은 아직 이 실행 서비스를 등록하지 않으므로 연결 설계가 필요하다. 공유 등록을 추출할지 여부는 실제 중복과 책임에 따라 판단하며 새 추상화를 전제하지 않는다.

현재 WPF `Stop()`은 Phase0의 도구 탐색 취소용이다. 다운로드 작업이 추가되면 창 닫기·취소·정리 완료 대기·DI/logger 종료 순서를 별도로 설계해야 한다. source/media 작업이 살아 있는데 services를 dispose하는 흐름을 그대로 재사용하지 않는다.

## 5. 그대로 유지할 실행·표시 규칙

- URL과 source의 검증, 완료 영상 상태, 선택snapshot·quality/mode identity, FPS 미확인 처리와 명시적인 pending checks를 유지한다.
- 확인 전 출력/source 파일을 만들지 않는다. 기존 prepared 요약의 실제 선택·주의·예산·미확인 검사를 UI에 연결한다. 확인 UX의 모양은 아직 설계하지 않았다.
- 일반 진행 단계 `Completed`나 Percent100을 최종 성공으로 해석하지 않는다. 정리와 최종 `ClipJobResult`까지 확인한다. 전체 작업 비율을 임의로 합산하지 않는다.
- Fast 실제 시작/끝은 요청과 다를 수 있다. Accurate도 검증된 frame/sample-grid 표현 범위다. 성공 화면은 요청과 실제 범위를 구분한다.
- output 기존 파일은 덮어쓰지 않는다. 경로는 준비와 실행에서 검사하며 오류·경고·잔존 파일을 최종 결과와 구분해 표시한다.
- 취소는 프로세스·파일 정리가 끝나야 완료된다. Finalizing 이후 늦은 취소는 확정 결과를 따른다. 종료/파일 수명 미확인과 일반 취소·정리 실패를 섞지 않는다.
- 외부 raw stderr/metadata·signed URL·인증값을 사용자 로그에 그대로 복제하지 않는다. code/category/field와 안전한 message를 활용한다.
- 기존 도구 탐색과 실행/버전/기능 진단은 다른 계약이다. WPF의 파일 발견을 실행 가능성 검증 성공으로 표시하지 않는다. 자동 설치·도구 업데이트는 없다.

## 6. 알려진 제한과 설계 시작 조건

| 제한 | Phase2 설계에서 다룰 영향 |
|---|---|
| 일부 CHZZK MPD metadata `sourceURL` 예외 | metadata 오류 상태와 재입력/재시도 UX. 사이트 전체가 미지원이거나 임의 URL이 보장된다고 표시하지 않음 |
| 별도 playlist2MiB 검사·64MiB/60초 CLI 기본 획득 예산·계획segment64개 | 출력 길이와 획득 예산을 구분. UI의 적용 예산/지원 안내를 설계하되 임의 확대나 사용자 설정 옵션 추가를 전제하지 않음 |
| Accurate source-start decode | 먼 위치 요청은 현재 실패할 수 있음. 46분 사례는 별도 지원 확대 작업이며 UI가 자동 Fast로 바꾸지 않음 |
| codec/edit/clock·AAC 보정·gap/overlap의 제한 증거 | 기존 media gate 실패를 UI로 전달. 임의 tolerance로 성공 처리하지 않음 |
| NativeEnd Unknown·remote 전체 동일성 미확인 | SourceEndVerified=false를 보존하며 선택 화질의 metadata 선언과 실행 검증을 구분 |

Phase1 필수 구현/실제 검증/사용자 재생과 완료 태그는 확보돼 Phase2 상세 설계의 기준 소스를 식별할 수 있다. 위 제한은 UI 상태·오류·주의 표현에 영향을 주는 선행 제약으로 기록한다. 현재 검증 범위의 WPF 연결 설계를 시작하기 위해 임의로 MPD/먼 위치 지원을 완료할 필요는 없지만, Phase2 목표를 범용 입력 지원으로 바꾸면 명세 영향 분석과 선행 media 보완이 먼저다. 무조건 모든 Phase2 시작 조건이 충족됐다고 선언하지 말고 실제 요구와 이 제한을 대조한다.

## 7. 새 설계 context에서 정리할 결정과 검증 계획

1. 입력·metadata 조회·Prepare·확인·Execute·취소·정리·최종 결과·재실행/창 종료의 상태 전이와 command 활성 조건.
2. 편집 중 입력과 고정된 준비 자료의 관계, stale async 결과 무시·중복 실행 방지, UI thread로 progress 전달·await/예외 처리를 포함한 수명.
3. HH/MM/SS/FF 입력·FPS 누락·quality 후보·mode·파일 선택·동적 validation 및 오류 접근성.
4. 단계/불확정 progress·실제 범위·주의/미확인 검사·잔존 자원·실패 field를 사용자에게 전달할 최소 UX.
5. WPF DI/logging·도구 경로·적용 획득 budget과 CLI 계약의 일관성, shutdown 대기 정책.
6. 논리적 구현·검증 순서와 commit 단위. 사용자 방침은 단계별 보고와 필요한 논리 commit 자동 진행이지만, 이번 handoff만으로 구현 권한을 새로 부여하지 않음.

검증 계획은 VM/서비스 상태 전이·validation·취소/종료 경합 테스트, 실제 WPF responsiveness/중복 클릭/편집/파일 선택/창 닫기, 도구/URL/metadata/예산 실패, 양 사이트·양 mode의 지원 가능한 실제 생성·재생·동기·소유 자원 정리를 포함해야 한다. 기존1195개 회귀·CLI 동작과 Phase0 도구 탐색/시작·종료/로그의 회귀를 보존한다. 실제 영상은 소리·움직임이 있는 자료로 선택하며 무음 영상의 decode 성공을 사람 동기 평가로 대체하지 않는다.

Phase2 상세 설계 결과물은 범위·확정/미확정 결정·실제 공개 계약과 책임·state/lifecycle·오류 UX·검증 계획·구현 순서·선행 차단 여부를 갖춰야 한다. 문서를 만든 것과 설계 채택·구현 완료를 구별한다.

## 8. 새 context 시작용 요청 예시

> 이 context를 ClipDownloader Phase2 WPF MVP 상세 설계 담당으로 사용할 예정이다. 먼저 docs/ClipDownloader/phases/PHASE_2_DESIGN_HANDOFF.md와 연결된 요구사항·운영 규칙·Phase1 최종 검증 인계를 읽어줘. 기준은 phase-1-complete가 가리키는4509f54dadbf140f787bdfd4a16dcc2b1f964c48이며 실제 Git·WPF/Application 소스와 미커밋 변경을 확인해줘. 이미 채택한 일반 정책을 다시 승인받지 말고, WPF 상태/비동기 수명·확인/실행·취소/종료·오류/실제 범위 UX·검증 계획·구현 순서를 설계해줘. 설계 중에는 제품 코드·프로젝트 생성·패키지/도구 설치·영상 다운로드를 하지 마. 미확인 media 지원을 추정으로 확정하거나 Phase3 이후·ClipEditor를 진행하지 마. 사용자 파일과 문서 변경을 보존해줘.
