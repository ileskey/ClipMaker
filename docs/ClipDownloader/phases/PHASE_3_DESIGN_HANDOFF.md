# ClipDownloader Phase 3 설계 context 인계

작성일: 2026-10-15. 대상은 **Phase 3 — Metadata UI 상세 설계 준비**다. 이 문서 작성으로 상세 설계를 확정하거나 구현을 시작하지 않는다.

## 1. Git 기준과 시작 확인

| 구분 | 기준 |
|---|---|
| Phase 2 상태 | Completed / Passed |
| Phase 2 Base | `3cbb04ed09df07d75dc5b963905b497909d8daeb` |
| Phase 2 Completion | `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f` |
| 완료 태그 | `phase-2-complete` → 위 Completion |
| 검증된 제품 소스 커밋 | `86fd9f8014aba1135fe30ba6d5b14be4df3ab152` |
| 이 인계 작성 시작점 | `phase/2`, HEAD `183532dabf3f00a9e42e30fbad1edc87f7f4c788`, 작업 트리 clean |
| Phase 3 브랜치·Base | 미지정. 설계 context에서 운영 규칙과 실제 Git 상태를 확인해 결정 |

`183532d`는 완료 기록을 정합화한 문서 커밋이다. Completion 이후 제품 소스 변경은 없다. 인계 작성 시작 시 로컬 `origin/phase/2`는 Completion이며 현재 브랜치는 ahead 1이었다. 실제 원격 서버는 재조회하지 않았다. 이 문서 커밋 후 HEAD는 달라지므로 위 작성 시작점을 최신 HEAD로 가정하지 않는다.

새 context에서 브랜치·HEAD·완료 태그·staged/unstaged/untracked 변경과 Completion 이후 diff를 다시 확인한다. 미커밋 사용자 변경과 `.artifacts`의 실제 검증 산출물을 보존한다. 과거 커밋 해시 변경 대응과 동일 tree 검증은 [Phase 2 구현·검증 기록](PHASE_2_IMPLEMENTATION.md)을 따른다.

## 2. 읽을 문서

1. 사용자 지시와 적용되는 `AGENTS.md`, [Phase 설계 운영 규칙](../PHASE_DESIGN.md), [Baseline](../PHASE_BASELINE.md).
2. [구현 계획의 Phase 3](../requirements/03_IMPLEMENTATION_PLAN.md), [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처](../requirements/02_ARCHITECTURE.md).
3. [프로젝트 구조 설계](../PROJECT_STRUCTURE_DESIGN.md), [Codex 작업 지침](../requirements/README_FOR_CODEX.md), [저장소 지침](../requirements/README_FOR_REPOSITORY.md). 과거 시점의 미구현 설명은 현재 소스와 대조한다.
4. [Phase 2 상세 설계](PHASE_2_DESIGN.md), [실제 구현·검증 기록](PHASE_2_IMPLEMENTATION.md), [미디어 증거 JSON](evidence/PHASE_2_WPF_MEDIA.json).
5. 미디어 지원 경계 확인이 필요하면 [Phase 1 최종 인계](PHASE_1_FINAL_VERIFICATION_HANDOFF.md). [Phase 2 설계 인계](PHASE_2_DESIGN_HANDOFF.md)는 구현 전 이력으로 읽는다.

## 3. Phase 3 범위와 현재 구현의 차이

요구사항의 표시 대상은 **Title, Uploader, Duration, FPS, Thumbnail, Available resolutions**다. 최소 완료 예시는 Title·Duration·FPS·Resolution이지만, 이를 근거로 Uploader와 Thumbnail을 조용히 제외하지 않는다. 누락 가능한 원본 필드와 표시 실패 처리까지 설계한다. metadata 실패 시 이전 정보가 남지 않아야 한다.

| 항목 | 현재 Phase 2 | Phase 3 설계 과제 |
|---|---|---|
| 제목·길이 | `MetadataText` 문자열 요약 | 상세 필드 표시·누락·긴 텍스트 처리 |
| FPS·해상도 | `SelectionText` 선택 요약, quality 선택 목록 | 선택 format과 연결된 표시, 사용 가능한 해상도의 의미·중복 처리 |
| Uploader | 모델에 존재하며 `MetadataText` 요약에도 표시 | 상세 필드 표시와 누락 처리 |
| Thumbnail | 모델과 parser에 URL 존재 | 이미지 획득·표시·실패·취소·수명 전체 |
| 재조회·실패 | 이전 metadata/선택을 먼저 비움 | 상세 필드와 이미지에도 같은 stale-result 방지 적용 |

Queue·Player·ClipEditor·내장 브라우저·도구 자동 설치는 범위 밖이다. Phase 3 표시를 위해 미디어 quality/mode·획득 예산이나 기존 지원 경계를 바꾸지 않는다.

## 4. 실제 연결 지점과 데이터 계약

경로는 저장소 루트 기준이다. 구현 전에 해당 소스를 직접 읽는다.

| 위치 | 확인할 책임 |
|---|---|
| `src/ClipDownloader.Application/Contracts/` | `IVideoSourceParser.Parse(Uri)`, `IVideoInfoService.GetVideoInfoAsync(Uri, CancellationToken)` 등 실제 인터페이스 |
| `src/ClipDownloader.Application/Models/VideoInfo.cs` | nullable Title·Duration·Uploader·ThumbnailUrl·UploadDate, State·Formats·MetadataIssue |
| `src/ClipDownloader.Application/Models/FormatSelection.cs` | RequestedQuality·VideoFormat·AudioFormat·Warnings·RankingCriteria |
| `src/ClipDownloader.Infrastructure/YtDlp/YtDlpMetadataParser.cs` | thumbnail URI 필터와 필드별 issue 처리 |
| `apps/ClipDownloader/ViewModels/MainViewModel.cs` | metadata/job/lifetime 상태 축, task·CTS·generation 소유 |
| `apps/ClipDownloader/ViewModels/MainViewModel.Input.cs` | metadata 조회, revision 검사, quality/FPS와 입력 검증 |
| `apps/ClipDownloader/ViewModels/MainViewModel.Job.cs` | Prepare·확인·한 번 Execute·최종 결과·정리 |
| `apps/ClipDownloader/Views/MainWindow.xaml` 및 `.xaml.cs` | 표시·포커스·스크롤·비동기 종료 glue |
| `apps/ClipDownloader/App.xaml.cs` | DI와 logger 종료 순서 |
| `tests/ClipDownloader.Tests/` | fake 서비스·STA/Dispatcher·실제 Window 종료 테스트 |

`VideoInfo`에는 영상 전체의 대표 FPS가 없다. `VideoFormat`의 nullable FrameRate/Width/Height와 선택 결과를 사용하며, rational FPS의 의미를 유지한다. 미확인을 0이나 임의 FPS로 바꾸지 않는다. format 선언과 선택 순위는 실제 실행 성공 보장이 아니다. `MetadataIssue(Field, Reason, FormatIndex?)`도 표시 정책 설계에 고려한다.

현재 thumbnail parser는 공백·역슬래시가 없고, 정상 형식의 절대 HTTPS DNS 호스트 URI이며 userinfo·`@`·query·fragment가 없는 값만 허용한다. 따라서 일부 원본 thumbnail URL은 이미 null/issue가 될 수 있다. 이 필터는 HTTP 획득·redirect·이미지 decode 안전성을 보장하지 않는다. URI 정책 완화가 필요하다고 판단하면 영향과 검증을 별도 설계 결정으로 기록한다. 현재 WPF에는 thumbnail 획득·이미지 표시 파이프라인이 없다.

## 5. 반드시 보존할 상태·수명 계약

- metadata `Empty/Loading/Ready/Failed`, job `Idle/Preparing/AwaitingConfirmation/Executing/Cancelling/Completed/Failed/Cancelled`, lifetime `Running/Closing/Closed`를 구분한다. 새 이미지 상태가 이 축들을 어떻게 사용하는지 명시한다.
- 동기 service preamble도 UI를 막지 않도록 worker에서 처리한다. 작업 상태를 await 전에 확보하고, 종료·generation·입력 revision·취소 검사를 통해 오래된 응답이 현재 상태를 덮지 못하게 한다.
- 같은 URL 재조회도 기존 metadata·선택을 즉시 비운다. Ready는 현재 Completed 영상과 양수 duration 검증을 전제로 한다. 성공 시 Start/End는 0, quality는 Best로 초기화하며 output·mode는 보존한다.
- quality 변경 시 입력 숫자는 유지하고 선택 FPS로 재검증한다. FPS 미확인 경로의 pending range 검증을 유지한다.
- 입력 변경은 준비를 무효화한다. 동일 `PreparedClip`은 한 번만 실행한다. progress나 파일 존재가 아니라 **최종 반환 결과**로 성공을 판단한다.
- 취소 요청만으로 작업 종료를 선언하지 않는다. 실행·취소·정리를 기다리고 잔존 자원·종료 미확인 경고를 보존한다.
- 닫기 task와 반복 Close 대기를 유지한다. MainWindow의 Dispatcher 지연 Close와 App의 DI 해제 → 종료 로그 → logger/self-log 해제 순서를 보존한다. 새 이미지 작업·stream·이미지 자원도 소유자와 종료 대상을 명확히 한다.

## 6. 기존 검증 근거와 한계

Phase 2의 실제 결과는 솔루션 build 경고/오류 0, 기존 테스트 1,195개와 WPF 35개, 총 **1,230개 통과·실패/skip 0**이다. 첫 커밋의 직접 helper 검사 9개는 이 합계와 별도다. 결과·명령·격리 스냅샷 검증은 Phase 2 기록에 있다. 이번 인계 작성에서 build/test를 재실행한 것은 아니다.

YouTube/CHZZK 각각 Fast/Accurate 네 클립을 실제 WPF에서 생성하고 전체 A/V decode를 확인했다. 사용자가 네 클립의 소리·움직임·동기를 모두 정상으로 확인했다. 파일은 저장소 `.artifacts/phase2-integration/`의 `youtube-fast.mp4`, `youtube-accurate.mp4`, `chzzk-fast.mp4`, `chzzk-accurate.mp4`이며 크기·SHA256은 증거 JSON을 따른다.

실제 작업의 UI 응답·취소·자식 프로세스 종료·작업 자원 정리를 확인했다. 반복 Close는 실제 WPF Window와 지연 fake 서비스로 검증했다. live 작업은 두 번째 닫기 전에 종료했으므로 live 반복 닫기 두 번을 관찰한 것으로 확대하지 않는다. 실제 프로세스 종료 실패를 강제한 것도 아니다. 경고·예외 분기는 fake/Dispatcher 검증 근거를 구분한다.

이 근거는 Phase 3의 새 상세 표시·이미지 획득 검증을 대신하지 않는다. 도구 버전은 다음 실제 검증 시 다시 확인한다. 격리 검증 중 네트워크 제한에 따른 NU1900과 일회성 NuGetAudit 비활성 옵션 이력이 있으므로 취약성 데이터 조회까지 통과했다고 주장하지 않는다.

일부 CHZZK MPD metadata 실패와 먼 위치 Accurate 제한은 기존 지원 경계다. 원본 획득 64 MiB/60초 예산은 출력 클립 길이 제한과 다르다. playlist 2 MiB·계획 segment 64개 및 codec/clock·원본 끝 미확인(`SourceEndVerified=false`) 경계도 유지한다. UI 작업 중 자동 fallback·quality/mode 변경·예산 상향으로 우회하지 않는다.

## 7. 상세 설계에서 결정하고 검증할 사항

1. 여섯 표시 항목의 모델 projection·레이아웃·누락/issue 표시·접근성, 선택 FPS/해상도와 available resolutions의 의미.
2. thumbnail 로딩·실패가 metadata Ready와 Prepare 가능 여부에 미치는 영향. 서비스/API 추가가 필요한지, 기존 책임 내에서 처리 가능한지 근거와 trade-off를 비교한다.
3. 이미지 획득·redirect·시간/바이트/메모리·decode 제한, 취소·오래된 응답 차단·stream 소유·종료 정리·캐시 필요 여부. 구체 라이브러리·수치·정책은 아직 결정하지 않았다.
4. URL 변경·동일 URL 재조회·실패·취소·닫기 중 새 표시/이미지 초기화와 작업 수명. Application/Infrastructure 계약 변경 시 CLI와 parser fixture 영향.
5. 독립적으로 검증 가능한 구현 순서, 각 단위의 build/test/실제 UI 기준과 문서·커밋 경계. 구현을 시작하면 단위별 검증·보고·커밋을 누락하지 않는다.

검증 계획은 양 사이트 정상 metadata, missing FPS/필드, quality 변경, 실패 후 이전 정보 제거, 느린/실패/잘못된/제한 초과 이미지, stale 응답, 취소·반복 닫기·정리를 포함한다. fake/Dispatcher 검증과 실제 WPF/원격 thumbnail 관찰을 분리해 기록한다. 기존 1,230개 회귀 검증과 실제 미디어 계약에 영향을 주는 변경의 재검증 범위도 정한다.

## 8. 다음 context 시작 프롬프트

아래 내용을 전달하고 이 문서를 먼저 읽도록 한다.

> 이 context에서는 ClipDownloader Phase 3 — Metadata UI 상세 설계를 진행해줘. 먼저 `docs/ClipDownloader/phases/PHASE_3_DESIGN_HANDOFF.md`와 연결된 운영 규칙·Baseline·요구사항·Phase 2 설계/실제 검증 기록을 읽어줘. Phase 2 Completion은 `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`, 태그는 `phase-2-complete`, 검증된 제품 소스는 `86fd9f8014aba1135fe30ba6d5b14be4df3ab152`야. 시작 시 현재 브랜치·HEAD·태그·미커밋 변경·Completion 이후 diff를 다시 확인하고 사용자 변경을 보존해줘. Phase 3 브랜치·Base는 아직 지정되지 않았어. 기존 metadata/선택 흐름을 확인하고 Title·Uploader·Duration·FPS·Thumbnail·Available resolutions의 표시와 실패·재조회·취소·오래된 응답·종료 수명을 설계해줘. 대표 FPS를 가정하거나 기존 thumbnail URI 정책을 조용히 완화하지 말고, 필요한 계약 변경과 trade-off를 밝혀줘. PreparedClip 한 번 실행·입력 변경 시 준비 무효화·최종 결과 기준 성공·정리 후 DI/logger 해제·FPS 미확인 경로·원본 획득 예산을 보존해줘. CHZZK metadata와 먼 위치 Accurate 제한은 기존 경계이며 Queue·Player·ClipEditor는 제외야. 논리 단위마다 검토 결과를 보고하고 `PHASE_3_DESIGN.md`에 결정·구현 순서·검증 계획·미해결 사항을 정리해줘. 이번 요청은 설계이며 제품 코드 구현·패키지 설치·미디어 다운로드는 시작하지 마. 후속 구현이 명시되면 단위별 검증·보고·커밋을 진행해줘.

## 9. 이 인계의 검증 범위

작성 시 Git 기준·현재 소스 계약·요구사항·Phase 2 완료 증거를 대조했다. 두 문서의 상대 링크 25개와 연결 소스 경로 11개, 완료 태그·제품 커밋 ancestry·Completion 이후 제품 소스 무변경을 확인했다. 새 build/test·미디어 생성·Phase 3 UI/thumbnail 검증은 수행하지 않았다. Phase 3 상세 설계 파일·제품 코드·프로젝트·패키지·브랜치·Base·태그를 이 인계에서 생성하거나 변경하지 않는다.
