# Phase 1 4a-3c-6 public selected inspection·소유 자원 인계

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `c042af9e7b0c65d54705ba529432507a3500c466`, branch `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **single HLS selected inspection adapter와 public resource 조회 구현/관련 검증 완료**. [native gate](PHASE_1_STAGE_4A3C5_NATIVE_RESULT.md)를 metadata 재검증·첫 참조 pipeline에 연결했다. target acquisition·YouTube indexed-source·4a 전체/처리 완료는 아니다.

SelectedSourceAcquisitionService는 현재 ISelectedSourceInspectionService/ISourceResourceAccess를 구현한다. public constructor는 기존 diagnostics/locator/settings/runner를 재사용하고, metadata resolver→bounded HLS collector/probe→native gate→immutable SelectedSourceInspection을 연결한다. 호스트 DI/CLI/처리 API를 연결하지 않았다. 내부 delegate constructor는 trusted test seam이다.

- 같은 job은 동일 immutable selection/workspace에만 결합된다. inspection은 instance에서 직렬화하며 취소를 처리한다. 종료 미확인 job은 다음 inspection을 실행하지 않는다. source cache/HTTP reader는 scoped instance 책임이며 host는 작업 종료 뒤 Dispose하고 같은 instance에서 resource 조회를 사용해야 한다.
- 확인된 first playlist reference의 native 최소 presentation을 선택 포맷 원점으로 반환한다. 원점 scope는 실제 가장 이른 stream에 연결한다. 한 muxed input의 검증된 무보정 movie/native 축 관계를 SharedAxis offset0으로 반환하며 stream 시작을 독립적으로0으로 만들지 않는다.
- known selected FPS와 source reported/measured rate가 충돌하면 VerificationFailed다. known 값은 바꾸지 않는다. 미상 FPS는 요청 purpose에 따라 reported rate 또는 exact bounded uniform frame spacing으로 조사한다. 보고/측정되지 않으면 Unknown이며30fps를 기본값으로 쓰지 않는다. FPS fact는 이 초기 관찰/선언 scope이고 global CFR proof가 아니다.
- native gate의 audio tracks와 metadata presence가 충돌하면 실패한다. 실제 absence와 unknown을 혼동하지 않는다. 초기 참조로 전체 source 끝을 확인하지 않아 NativeEnd는 Unknown/Partial로 유지한다. timeline을 요청하지 않은 경우 origin/axis는 NotInspected다.
- Application에 SourceOwnedResources/ISourceResourceAccess를 추가했다. public 실패/취소 Data=null 규칙을 유지하면서 caller가 job ID로 owned files·NeedsCleanup·HasUnconfirmedProcess·safe warning을 조회한다. production은 collected file을 probe 시작 전에 registry에 기록하고 collection cleanup 잔존 파일도 보존한다. 파일을 삭제/이동하지 않고 process 종료 미확인을 숨기지 않는다.
- private locators/raw metadata/native observations는 public descriptor에 노출하지 않는다. resource 조회의 local owned paths는 기존 OwnedMediaFile 계약이며 signed media URI가 아니다. 상위 workspace의 실제 cleanup/경고 표시·DI 등록은 후속 Application/host 단계다. resource 조회 없이 failed OperationResult만 소비하면 인계를 놓치므로 같은 instance를 연결해야 한다.

## 실제 검증

|실행|결과|
|---|---|
|Downloader Infrastructure tests `--no-restore`|최종 exit0,555 통과/0 실패/0 skip|
|`dotnet build ClipMaker.slnx --no-restore`|exit0,경고0/오류0|
|실제 CHZZK 15430558 public inspector|Succeeded, hls-8384·60/1 FPS·origin19187/9600·SharedAxis Verified·NativeEnd Unknown·owned file1|

새9개 사례는 scoped facts/end Unknown·missing FPS/known conflict·native failure·failure/cancel resource 조회·종료 미확인 재실행 차단·pre/post 취소·job 재결합 차단을 검증했다. 이전 native verifier fixture를 trusted result seam으로 재사용했고 신규 fixture는 저장소 내부 고유 폴더의 생성한 파일만 정리했다. 처음553개와 추가 후555개가 통과했고 새 실패/경고는 없다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

실제 실행은 기존 사용자 승인 URL에서 metadata 조회·best 선택·snapshot 생성 후 adapter의 metadata 재조회와 첫 selected HLS init/segment만 획득했다. 기존 yt-dlp/FFprobe·SDK를 사용했고 설치/cookies/remote components/전체 영상 fallback은 없다. generated `.artifacts/source-service-real/<GUID>`의 새 미디어2,050,886bytes와safe summary338bytes를 보존했다. snapshot/원점은 이 선택 resource의 확인 결과이며 다른 화질/player 기준을 보장하지 않는다. harness의3분 취소 제한은 실험용이고 제품 원격 획득 timeout으로 복사하지 않았다.

## 다음·커밋

다음은 같은 private inspection context·소유 registry로 target HLS 참조를 수집하고 source/local mapping 및 필요한 coverage를 대조하는 acquire adapter다. inspection 성공은 qualified processing/최종 clip 성공이 아니다. 이후 YouTube indexed-source와 source 변화/취소/HTTP·budget/resource 통합 검증까지 남아 있다.4a 단위별 자동 커밋을 유지한다.

범위: Application 계약/모델2·Infrastructure 신규1·test 신규1/기존1·결과/설계/source 계약/baseline4문서. 메시지: `feat: Publish scoped HLS source inspection and owned resources`.
