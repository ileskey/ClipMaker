# Phase 1 4a-2b selected inspection·획득 계약 연결

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `36136a2e4693c4f183cb6959160f500068749f13`, branch `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **4a-2b 계약 구현·관련 검증 완료**. [4a-2a](PHASE_1_STAGE_4A2A_RESULT.md)의 공용 모델에 [G5b](PHASE_1_G5B_API_READINESS.md)의 inspection/acquisition 책임을 연결했다. 4a-2의 공용 기본형·획득 계약 단위(2a/2b)를 완료했으며 실제 source/HTTP 어댑터·qualifier·probe/처리·4단계 전체/Phase 완료는 아니다.

## 계약과 불변식

Downloader.Application에 모델/validation4파일·서비스 계약2파일을 추가했다. token/progress는 메서드 인자이며 작업 snapshot에 프로세스·인증값·서명 URL을 저장하지 않는다.

- `SelectedSourceSnapshot`: 완료 영상과 기존 FormatSelection을 다시 구조 검증하고 local snapshot ID·canonical source·선택 포맷/quality·mode·metadata duration을 보존한다. snapshot ID가 같아도 source·mode·선택 format record/FPS·duration 등이 다르면 Matches=false다. 이 GUID를 서버 content generation 증명으로 사용하지 않는다.
- `SelectedSourceInspectionRequest`: JobId·선택 snapshot·FrameRate/SourceTimeline purpose·소유 작업폴더 절대경로·positive bounded byte/span budget. FPS 조사에는 ClipJob/MediaTimeRange를 요구하지 않으므로 확인후 검사→rate 확정→target 검증의 순환 의존이 없다. 이 계약 자체가 사용자 확인을 수행/생략하는 것은 아니며 Application의 확인후 실행 정책을 유지한다.
- `SourceTimelineDescriptor`/`SelectedSourceInspection`: 원점·shared axis·native end의 scoped facts, 원본 audio presence, effective FPS, 실제 관찰 파일을 보존한다. 사실은 동일 inspection ID·소유 file ID·선택 포맷 provenance·필요 native time basis와 연결한다. FPS는 video 관찰만 허용하고 shared-axis pair/absence와 선택의 구조적 모순을 차단한다. native end와 metadata duration은 별개다. 실제 parser의 증거 판정은 아직 없다.
- `OwnedMediaFile`: 절대 canonical path·file ID·선택 source format IDs. collection을 복사하고 중복/선택 밖 format·work-directory 밖/자기 directory path를 거절한다. frame rate가 split audio 파일에서 온 것으로 등록하면 허용하지 않는다. combined 파일에는 선택 pair의 여러 IDs를 표현할 수 있다. **경로 포함·identity/provenance 구조 검사**이며 실제 파일 존재/생성·filesystem 소유권을 증명하지 않는다. 실제 어댑터/workspace가 생성·추적하고 probe/qualification이 검사한다.
- `AcquisitionRequest`: 같은 job/selection/workspace의 inspection, 확인된 unchanged FPS와 metadata 길이 내 validated MediaTimeRange. 최초 metadata FPS Unknown을 검사로 해소하는 것은 허용하지만 알려진 rate의 변경·같은GUID로 다른source 선택·시간 범위 없는 target acquisition은 거절한다. 원점/shared axis 미확인 상태를0/true로 대체하지 않는다.
- `SourceStreamMapping`/`AcquiredMedia`: 별도 acquisition ID, 소유 파일과 mapping의 read-only snapshot. native source offset와 coverage는 같은 local stream/snapshot에 연결한다. verified coverage는 실제 종류의 complete frame/sample 창 안에 있어야 하며 verified B의 검사 창도 그 범위를 포함해야 한다. summary/demux/다른snapshot/다른file·duplicate mapping은 차단한다. Unknown/Contradicted를 보존하며 전달 성공/객체 생성은 qualified processing/clip 성공이 아니다. 아직 mapping이 없는 transfer snapshot도 표현 가능하고 필요한 mapping 부재는 후속 qualifier가 차단한다.
- `AcquisitionProgress`: job ID·nullable finite0~100 acquisition 단계 비율·안전한 메시지. 미확인 비율은null이며 임의0을 넣지 않는다. 메시지의 안전한 구성은 producer 책임이다.

`ISelectedSourceInspectionService.InspectAsync`와 `IClipDownloadService.AcquireAsync`의 async/cancellation/progress·OperationResult 경계를 선언했다. 실제 구현·DI 등록·ClipJob/Workspace skeleton·새 Application 오류 enum/패키지는 추가하지 않았다. constructor의 불변식 위반은 모델을 만드는 코드의 잘못된 조합을 거절하는 ArgumentException 계열이다. 외부 metadata/process 실패를 기존 OperationError로 번역하는 adapter/use-case 책임은 후속 구현이다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore` | exit0, 통과307/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

기존 Application 모델 테스트가 있는 Downloader.Infrastructure.Tests를 재사용했다. helper1파일·test2파일을 추가했고 새14개 사례가307개에 포함된다. FPS 미확인 요청·확인후 range, 같은ID의 선택 변경·wrong job/workspace/FPS·metadata 범위 초과, inspection/file/provenance/native basis 충돌, input collection 변경, lexical 경로 이탈·중복·budget/purpose를 검사했다. acquisition의 unknown/contradiction 보존·외부/중복 mapping·summary/부족한 coverage/offset 창·invalid/unknown progress도 검사했다.

테스트 fixture는 파일 경로/typed observations만 구성하고 작업폴더·미디어를 생성하지 않았다. 새 source/media/native 사실을 측정하지 않았으며, Core136개와 나머지 모든 프로젝트 테스트를 이번에 재실행했다고 보고하지 않는다. Core 소스는 변경하지 않았다. 솔루션의 WPF 빌드 성공을 실제 UI 동작 검증으로 설명하지 않는다.

## 다음 단계·커밋 지점

다음은 **4a-3 source inspection/획득 어댑터**다. 첫 논리 단위에서 bounded source/HTTP·실제 byte completeness·취소/retry·초기화/필요 source 참조·snapshot/provenance 생성 책임을 구체화하고 구현한다. 검증 범위의 adapter를 하나씩 연결하며 요청 확인 이전 media 획득·전체 다운로드 fallback·새 도구 설치/인증 의존성을 추가하지 않는다. 치지직 초기 source/native 조사와 구간 획득의 실험 보완책을 실제 제품 경로에 반영하는 작업은 여기서 이어진다.

native decoder 준비·coverage 증거 생산·최종 처리/검사는 여전히 남아 있다. 일부 constructor/state 테스트 성공으로 해당 준비 완료를 추정하지 않는다. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.

이번 커밋 범위는 Application6파일·test/helper3파일·결과/설계/source 계약/baseline4문서다. 신규 파일 공백·문서 링크·경계/변경 scope를 확인했고 staging/commit은 하지 않았다. 기존 사용자·실험 자료를 보존했다. 권장 메시지: `feat: Add selected source inspection and acquisition contracts`.
