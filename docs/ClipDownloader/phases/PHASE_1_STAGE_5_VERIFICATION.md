# Phase 1 5단계 구현·통합 검증 기록

2026-10-14, `phase/1`, 시작HEAD `241e44a`. 사용자 지시에 따라 **검증된4단계 전략을 연결한5단계(workspace·준비·Application 실행·정리·최종 확정)를 완료했다.** 아래 기록의 진행 중 설명은 해당 시점의 이력이며 마지막 완료 범위·6단계 인계를 현재 기준으로 읽는다. 논리 단위 자동 commit과 단계별 통합 보고를 유지한다. 6단계 CLI 연결·Phase 1 전체·Phase2/WPF 다운로드 UI/ClipEditor는 완료로 선언하지 않는다.

## 5a 출력 workspace/session

기존 정책대로 출력 경로를 준비 시 검증하고 실행 시 재검사한다. Windows reserved names/ADS/제어 문자·MP4 확장자·기존 parent/충돌을 검사한다. Path.GetFullPath가 trailing space/dot를 자동 제거하는 실제 반례를 발견해 원문 spelling부터 거절하도록 보완했다.

확인 후 workspace는 기본 LocalApplicationData/ClipMaker/ClipDownloader/jobs 아래 고유 작업 폴더를 생성하고, 출력 폴더의 고유 staging 경로·파일 ID를 제공한다. staging 파일 자체는 producer의 CreateNew 책임이다. 명시적인 producer resource만 작업 폴더/정확한 staging 범위에 인계하며, 생성 전에 충돌한 staging·사용자 파일·최종 파일을 cleanup 대상으로 추정하지 않는다.

최종 확정은 등록된 검증 staging의 ID/path/bytes/SHA를 확인하고 overwrite=false 이동을 한 번만 시도한다. 이동 전후 content를 대조한다. 반복 commit은 programming error다. cleanup은 취소 token을 받지 않으며 소유 파일만 개별 삭제하고 작업 폴더는 비재귀 empty delete한다. 종료 미확인 보존 모드에서는 삭제하지 않는다. 정리 경고/잔존 경로는 원래 결과와 별도로 전달한다. OperationResult의 실패 metadata 목록도 immutable snapshot으로 추가했다.

13개 실제 local filesystem 테스트로 입력 이름, 기존 최종 파일 충돌, staging bytes 변경, 성공 후 final 유지, 잠긴 파일의 cleanup warning, 미등록 파일 보존, 반복 정리·commit을 검증했다. 초기 테스트의 정리된 parent 아래 없는 파일을 다시 삭제하는 fixture 오류는 수정했다. 이후13개 통과, 관련 Application/Infrastructure build 경고·오류0. root를 주입한 소유 GUID test 폴더를 사용하며 native tool·영상·패키지 설치는 없었다. Application 준비/실행 조정·실제 양 사이트 통합은 다음 단위에서 진행 중이다.

## 파일 닫힘 미확인 인계 보완

5단계 통합 audit에서4단계 collector가 닫힘 실패 후 보존한 파일의 이유를 외부 owner가 알 수 없었던 점을 확인했다. source OwnedMediaFile에 MayBeInUse, shared processing resources에 HasUnconfirmedFileHandles를 추가했다. HLS/indexed/보조 collector와 PCM/FFmpeg 종료 처리에서 이 정보를 보존한다. process 종료 미확인과 file handle 미확인을 구별하여 정상 취소130을 process 실패로 바꾸지 않으면서도 사용 중일 수 있는 파일을 삭제하지 않는다.

기존 indexed/보조 close/close-cancel/delete lifecycle tests에 보존 flag assertion을 추가해 닫힘 미확인만 flag가 켜짐을 검사했다. 현재 Downloader806개와 최신 Media Infrastructure143개를 실제 build/test로 통과했다. 단순 delete 실패는 원래 cleanup 경고로 구별한다. 새 추출 방식·fallback·범용 decoder 준비 조건을 추가하지 않았다.

## 5b 준비와 5c Application 조정

IClipJobService와 immutable ClipRequest/PreparedClip/ClipJobResult/JobProgress를 구현했다. 준비는 기존 URL parser로 요청과 metadata source를 대조하고, 완료 영상/길이·명시 선택·시간 syntax/semantic·출력 경로·필수 도구/기능을 검사한다. 쓰기·workspace/source 작업은 하지 않는다. FPS 미확인에서는 시간 성분과 미확인 검사를 보존한다. 제목/업로더·선택 snapshot·절대 출력·획득 budget을 준비 결과에 고정한다.

실행은 준비 job을 한 번만 소비한다. 출력·도구·metadata를 재검사하며 같은 source/format/선언 조건을 요구한다. Best 재선택·FPS 의미의 조용한 변경·자동 mode/quality fallback은 없다. unknown FPS→검사로 확인한 FPS는 미확인 항목 해소로 구별하여 구간을 검사한다. source inspection/acquisition/verified output의 요청 identity도 대조한다.

취소 callback과 Finalizing 전이는 한 gate에서 조정한다. 전이 전에 수락한 취소는 commit 없이 정리하며, 전이 후의 취소는 commit 결과를 따른다. 반복 실행/commit은 programming error다. 예상 실패·취소는 최종 결과로 반환하며 source와 media registry를 성공/실패/취소 모두 인계한다. 종료 미확인은 Failed·보존·동일 서비스 후속 차단, 파일 닫힘 미확인은 원래 실패/취소 상태를 유지하면서 보존한다. 정리 warning은 원래 결과를 덮지 않는다. cleanup progress callback의 programming error가 나더라도 정리는 finally에서 수행한다.

부분 workspace 생성 후 취소에서 폴더 삭제가 실패하는 경우도 취소 상태를 유지하도록 cancellation metadata를 연결했다. 전체 Application 재시도/파일 접근 재시도는 없다. ClipJobResult는 성공한 최종 경로만 공개하며 cleanup 이후 사라진 입력 경로/processing plan 대신 사용자 원점 기준 요청·actual video/audio 범위와 sample 보정/skip/tail 요약을 제공한다.

20개 조정 테스트에서 확인 전 쓰기 없음, invalid source/time/range/출력/영상 상태·기능 거절, FPS 변경/unknown 해소, 확정 전후 취소, 이동 충돌의 사용자 파일 유지, 종료 미확인·file handle 보존, media 실패, 성공 후 cleanup warning, 부분 workspace 취소 metadata를 검증했다. fake verified media는 orchestration만 검사하며 실제 미디어 성공으로 대신하지 않는다. workspace13개·조정20개·media composition3개 추가, Downloader807개 실패·skip0. 솔루션 build 경고·오류0, Core143/Media Infrastructure143개 실제 실행 통과.

## 4단계 media adapter와 실제 최종 작업

ClipMediaService는 기존 공유 probe/native binder·K/qualifier·source-start decode/PCM render·processing·Fast/Accurate verifier를 순서대로 연결한다. 생성 operation ID와 모든 파일/종료 미확인 정보를 작업별 registry에서 조회한다. 실패한 probe 이후 처리를 하지 않고, 종료 미확인 후 재사용을 차단하는3개 facade 테스트를 추가했다. 새로운 source 전략이나 최종 출력 검사 우회는 없다.

사용자가 허용한 bounded10~20초 범위로 Application Prepare→Execute를 양 사이트/양 모드에서 실행했다. 실제 기본 app-data jobs 아래 원본/PCM을 만들고, 출력 폴더 staging을 검사한 뒤 최종 clip.mp4로 확정했다. 준비 직후 final 없음, 성공 후 final 존재/기대 hash 유지, 모든 소유 임시 파일·job 폴더 정리를 확인했다. [safe 증거](evidence/PHASE_1_STAGE_5_REAL_JOBS.json)에 네 작업의 결과·hash·파일 수·경고·표시 범위와 이후 재검사를 기록한다.

| 사이트/모드 | final bytes/SHA256 | source/media 소유 파일 | 잔존 파일/작업 폴더 |
|---|---|---:|---|
| CHZZK Fast | 11281027 / `360d6fba66306f1427f6220f076ea8f9ec7294e459bb2cd0b76ff87bcc09cdfe` | 3 / 1 | 0 / 0 |
| YouTube137/140 Fast | 3469926 / `52314b66092c858dd4521d8e372a5546d2bc4da5e381eea87a16d0fd7d98ddd8` | 10 / 1 | 0 / 0 |
| CHZZK Accurate | 1173183 / `3916abad8217623b35079f2502c185444cac487b56107cb0fd9aaa6714612dde` | 3 / 3 | 0 / 0 |
| YouTube137/140 Accurate | 4897957 / `c599d0e9beda14a895ac0003f0607aa21b0fa010385f325452742707867801ce` | 10 / 3 | 0 / 0 |

최초 CHZZK Fast harness는4단계 source 좌표 Video/Audio를 기록했고 이후 세 작업은 현재 API의 사용자 좌표 Presentation을 기록했다. 기존 기록을 임의 원점으로 바꾸지 않는다. CHZZK Accurate의 사용자 영상 범위는[96013/9600,192013/9600), 음성[10,20), gap 삽입2/중복 제거1·AAC skip1024/tail256이다. YouTube Accurate는 영상/음성[10,20), skip1024/tail344다. 요청 길이와 video 실제 표시 끝을 같다고 설명하지 않는다. 모든 SourceEndVerified는 false를 유지한다.

ignored RunStage5Job.ps1은 UI 확인이 끝난 호출에 해당하는 Execute를 직접 실행했다. 최초 harness의 PowerShell null→빈 string 변환으로 workspace root 전달이 실패한 실행은 성공으로 세지 않았다. .NET 기본 app-data 경로를 명시해 재실행한 위 네 작업이 성공했다. 데이터/metadata/CLI 확인 계약을 우회하지 않았으며 CLI clip/Y/N/--yes 동작의 검증으로 승격하지 않는다. 설치·whole-video fallback·모드/화질 변경·기존 사용자 파일 덮어쓰기·삭제는 없었다.

폴더 독립 재검사의 첫 실행은 sandbox에서 redirect된 LocalApplicationData 경로를 조회해 경로 없음 오류가 났다. 이 실행의 폴더 확인을 유효 증거로 사용하지 않고, 실제 native 작업과 동일한 실행 환경에서 다시 검사했다. 실제 root `C:\Users\shiro\AppData\Local\ClipMaker\ClipDownloader\jobs`가 존재함을 요구하고, 네 job ID별 폴더0·최종 hash 일치를 확인한 뒤 safe 증거의 WorkRoot와 결과를 정정했다. 원래 최종 파일과 이력은 보존했다.

## 5단계 완료 범위와 6단계 인계

- 준비의 source/시간/경로/선택/필수 capability 검사와 immutable 확인 자료, confirmed/unknown FPS 구별을 완료했다. 준비에서 쓰기·source 획득은 없다.
- 실행 재검사·선택 고정·source/media 요청 identity·검증 staging 연결·한 번 최종 확정·자원 인계/정리와 취소 경합을 완료했다. 기존 사용자 파일과 확정한 final은 cleanup하지 않는다.
- 같은 process 종료 미확인은 Failed 및 보존/후속 차단, 파일 닫힘 미확인은 원래 결과 및 보존, 단순 cleanup 실패는 warning/잔존 경로다. 중단 token으로 cleanup을 생략하지 않는다.
- 양 사이트/양 모드 Application 작업에서 final MP4 확정과 소유 임시 파일/폴더 삭제를 실제 검증했다. 4단계 codec/edit/clock·source-start budget·delay/empty support의 지원 제한과 NativeEnd Unknown·lossy 값 비동일성·player 미검증은 유지한다.

최종 검증은 `dotnet build ClipMaker.slnx --no-restore` 경고·오류0, 각 기존 test csproj의 `dotnet test --no-restore --no-build`로 Core143/Media Infrastructure143/Downloader807개, **총1093개 실패·skip0**다. 단위 검증 뒤 변경된 cancellation metadata까지 솔루션을 다시 build해 같은 assembly 버전으로 회귀 검증했다. native harness가 로드한 DLL을 빌드로 덮어쓰지 않도록 실행 순서를 구별했다.

다음6단계는 기존 CLI/Core Prototype 호스트의 실제 상태부터 확인하고 diagnose/info/clip 옵션·Application 조합·도구 경로·Y/N/EOF/--yes·Ctrl+C·0/1/2/130·요청/actual 범위·warning/잔존 경로 표시·로그·수명을 연결한다. 5단계는 콘솔 입력/숫자 종료 코드/DI 호스트 정책을 갖지 않는다. 6단계에 자동 진입하거나 WPF 다운로드 UI·ClipEditor를 수정하지 않았다. Source acquisition과 media facade는 해당 명령에서 같은 인스턴스를 사용해 registry 조회를 연결하고, 명시 cleanup 후 owner의 수명을 종료해야 한다.

Git은 `phase/1`을 유지하고 승인한 논리 단위로 자동 commit했다. 문서 기준 `0759dc5`와 현재 실제 선행 Phase 0 `1ca2547`의 동일 source tree/다른 ancestry는4단계 기록을 유지하며 hash를 교체하거나 이력을 재작성하지 않았다. Phase 1 Base/Completion/Tag/Passed는 임의 갱신하지 않는다.
