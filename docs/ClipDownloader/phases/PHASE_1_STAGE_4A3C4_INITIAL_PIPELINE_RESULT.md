# Phase 1 4a-3c-4 HLS 초기 참조 검사 흐름 연결

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `3a4cd9e41b87faf3c615b3e43e910166a2a2194e`, `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지. 자동 커밋 범위는4a이고 push/4b/4c는 포함하지 않는다.

상태: **HLS resolved selection→playlist→첫 whole reference 계획→소유 파일 수집→bound-file probe 연결 구현/관련 검증 완료**. [packet 관찰](PHASE_1_STAGE_4A3C3C3B2_PACKET_RESULT.md) 이후 native gate 전에 기존 구성요소와 resource handoff를 묶었다. internal 관찰 adapter이며 public ISelectedSourceInspectionService/원점/FPS fact 발행·target acquisition/4a 전체 완료가 아니다.

`HlsInitialReferenceInspector`는 이미 재검증된 private SelectedSourceLocators와 inspection request를 받는다. snapshot Matches·source/completed/duration·video format ID를 확인하고 단일 선택 HLS leaf에만 진행한다. metadata resolver를 대체하거나 format 재선택/분리 A/V fallback을 하지 않는다. actual metadata 재조회는 upstream YtDlpSourceLocatorService 책임이다.

- 기존 caller 작업폴더가 있어야 한다. pipeline은 폴더를 만들지 않는다. playlist 읽기 cap은 budget/2MiB 중작은 값이며, 첫 segment의 exact EXTINF range만 whole reference planner에 전달한다. span 초과는 media 요청 전에 차단한다.
- collector에 playlist.SourceBytes를 alreadyReadBytes로 넘겨 init·첫 segment를 포함한 누적 source body budget을 유지한다. 첫 segment만 가져오고 나머지를 건너뛰어 coverage 성공이라고 처리하지 않는다. media sequence/playlist0은 source O가 아니다.
- production constructor는 InitialReferenceProbe를 연결한다. fake probe delegate/collector는 trusted composition/test seam이다. probe가 반환한 Reference는 collector의 동일 객체여야 하며 바꿔 반환하면 VerificationFailed다. 성공은 원래 소유 파일과 raw demux/container 관찰을 가진 후보뿐이며 native/지원 fact를 발행하지 않는다.
- collection 실패/취소의 원래 error/status·RemainingFiles를 그대로 보존한다. probe 실패/취소/collector 이후 취소에는 Data=null이고 생성한 파일을 RemainingFiles로 반환한다. 종료 미확인 실패는 취소로 덮지 않는다. probe 이후 pipeline에서 파일을 삭제하지 않는다. caller는 process 종료 확인/소유 수명에 따라 cleanup하고 safe warning/remaining 경로를 상위 작업에 연결해야 한다.
- 성공 파일은 Data.Reference.File로 caller에게 소유 인계된다. 내부 result와 RemainingFiles를 버리고 public OperationResult로 단순 변환하는 adapter는 아직 만들지 않았다. caller의 workspace registry/public 실패의 cleanup 전달 경계를 함께 닫아야 한다. raw signed URI/header/probe 내용은 public/log에 노출하지 않는다.

## 실제 검증

|실행|결과|
|---|---|
|Downloader Infrastructure tests `--no-restore`|최종 exit0,520 통과/0 실패/0 skip|
|`dotnet build ClipMaker.slnx --no-restore`|exit0,경고0/오류0|

새13개 사례는 fake HTTP와 실제 tiny 소유 파일로 같은 선택·playlist budget·첫 init/segment 순서·probe 인계·foreign snapshot/transport·byte/span budget·missing 참조·pre/post 취소·termination 우선순위·collector cleanup 잔존파일·probe reference 교체 차단을 검사했다. 첫515개는 통과했다. 추가 test의 중첩 괄호 구문 오류1개를 수정한 최종520개가 통과했다. 테스트 fixture가 생성한 저장소 내부 고유 `.artifacts/initial-inspector-tests/<GUID>`의 immediate files/folder만 정리했다.

이번 단위의 production pipeline 실제 site/local-media 통합 실행은 하지 않았다. 이전 구성요소의 실제 FFprobe/로컬 oracle 검증을 이번 pipeline 실행 성공으로 대체하지 않는다. network·영상 다운로드·설치·새 프로젝트·사용자/기존 실험 파일 변경은 없다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

## 다음 단위·커밋

다음은 codec/container sample timing과 native evidence/미확인 gate다. 그 뒤 public selected inspector의 fact/소유 실패 인계와 CHZZK target mapping/coverage, YouTube indexed 획득·source 변화/취소/잔존 resource 통합 검증이 남아 있다. 첫 참조 transfer/probe 완료는 native origin/decoder 준비/4a 완료와 다르다.

커밋 범위: Infrastructure 신규1·test 신규1·결과/설계/source 계약/baseline4문서. 자동 커밋 메시지: `feat: Compose bounded HLS initial reference inspection`. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.
