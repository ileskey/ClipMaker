# Phase 1 6단계 — CLI 연결·통합 검증

최신 CHZZK Accurate 대체자료(2026-10-15): [15537558·45~55초 검증](PHASE_1_CHZZK_AUDIBLE_ACCURATE_VERIFICATION.md)의실제CLI·기존gate·독립full decode·정리가성공했다. 600서로다른decoded picture hash와무음아닌음성signal을확인했으나사용자재생/음성동기는아직Pending이다. 앞선무음자료의한계를이대체자료로확인할예정이며Phase1전체완료를아직확정하지않는다.

최신 소리 있는 후보 시도(2026-10-15): 사용자제공15654475의20~30초Accurate 실제CLI는metadata sourceURL 예외로exit1·final없음이다. [기록](PHASE_1_CHZZK_ACCURATE_PLAYLIST_DIAGNOSIS.md#새-후보15654475-실제-시도)을따르며CHZZK Accurate 자동생성/사람재생통과로세지않는다. Phase1전체완료는Pending이다.

최신 재생 자료 보완(2026-10-15): 사용자가보존CHZZK Accurate의방송초반이무음이라음성동기확인에부족하다고보고했다. 자동생성/native/decode기록을사람검증으로승격하지않는다. 사용자는소리·움직임이있는다른영상의앞쪽구간으로현재지원범위를검증하기로선택했으며새URL/구간을기다린다. [후속 기록](PHASE_1_CHZZK_ACCURATE_PLAYLIST_DIAGNOSIS.md#후속-사용자-입력과-검증-자료-변경)에14791589의46분Accurate1396/64 segment 거절도기록했다. 전체Phase완료는Pending이다.

최신 CHZZK Accurate 시도(2026-10-15): [새입력진단](PHASE_1_CHZZK_ACCURATE_PLAYLIST_DIAGNOSIS.md)에서15578184는metadata sourceURL 예외,15683602는별도playlist2MiB상한초과임을확인했다. 46분Accurate는현재source-start획득예산밖이며양실패를재생확인으로세지않는다. CHZZK Accurate 사용자확인은아직남아있다.

최신 CHZZK Fast 재생 확인(2026-10-15): 사용자가14791589·hls-8384·60fps·46:10~46:40·Fast의 `out6.mp4` 재생·음성 동기를 확인했다. [기록](PHASE_1_CHZZK_FETCH_BUDGET_SUPPORT.md#후속-사용자-생성-확인)과 YouTube 양 모드 사용자 확인을 보존한다. CHZZK Accurate의 사용자 확인·Phase1 전체 완료 판정은 남아 있다.

최신 CHZZK 입력 진단(2026-10-15): 새14327648은 [yt-dlp MPD metadata 추출 실패](PHASE_1_CHZZK_MPD_METADATA_DIAGNOSIS.md)로 구간 획득 전에 차단된다. sourceURL 없는 Initialization과 직접 속성 접근 예외를 확인했으며 이 영상 지원이나 CHZZK player 통과로 기록하지 않는다. 문서 진단만 추가했고 기존 HLS 성공 증거와1192개 회귀 결과는 보존한다.

최신 사용자 재생 확인(2026-10-15): 움직이는 YouTube `qnjEE-wkw8k`137/140·30fps·10~40초의 Accurate 통과 보고에 이어, Fast 생성 완료·재생 성공·음성 동기 통과를 [기록](PHASE_1_FAST_FRACTIONAL_SEEK_SUPPORT.md#사용자-재생동기-확인)했다. 해당 두 사례의 사용자 확인과 아래 기존 네 final 전체의 검증을 구별한다. CHZZK 양 모드의 사용자 재생·동기 확인과 Phase1 전체 완료 판정은 남아 있다. 최신 자동 회귀는1192개 통과다.

최신 Fast 후속(2026-10-15): [분수 seek 지원·실제 검증](PHASE_1_FAST_FRACTIONAL_SEEK_SUPPORT.md)에서 움직이는 `qnjEE-wkw8k`137/140·10~40초 Fast의 명령 표현 거절을 보완했다. 실제 CLI·기존 output gate·독립 전체 decode·정리를 통과했으며, 직접 player 확인은 남아 있다.

최신 후속 검증(2026-10-15): [H.264 recovery-point 지원·사용자 재생 확인](PHASE_1_H264_RECOVERY_POINT_SUPPORT.md)에서 움직이는 YouTube `qnjEE-wkw8k`137/140·30fps·Accurate10~40초의 실제 생성·decode와 사용자 재생 통과 보고를 기록했다. 최신 회귀는1185개 통과·build 경고/오류0이다. 이 사용자 보고는 해당 사례에 한정하며 아래 네 final 전체의 재생 확인을 완료한 것으로 세지 않는다. 아래 검증 수·상한·미완료 설명은 각 수행 시점의 이력이다.

최신 후속 지원: [AAC 시작 보정 지원·검증](PHASE_1_AAC_PRIMING_SUPPORT_VERIFICATION.md)에서 신고된source의1600 sample 보정과provider 관계·두mode의최종 확정·독립PCM 대조를 완료했다. 최신 회귀는1148개 통과이며 직접player 확인은 아직 남았다. 아래실패와검증 수는각수행시점의이력이다.

후속 사용자 입력 진단: 다른 YouTube source의 AAC1600 skip으로 초기 native/decoded frame 수가 달라지는 미지원 사례를 재현했다. [원인·오류 분류 보완·지원 잔여](PHASE_1_YOUTUBE_AAC_SKIP_DIAGNOSIS.md)를 우선해 읽는다. 성공 지원을 확대하지 않았고 후속 전체 회귀는1127개 통과다. 아래1121개와 네 실제 성공 사례는6단계 수행 시점의 유효한 기록이다.

수행일: 2026-10-14 (Asia/Seoul). 시작 HEAD `0e596ba`, branch `phase/1`, 작업 트리 clean에서 사용자의 6단계 진행 지시로 수행했다. 기존 자동 논리 커밋·bounded 다운로드 허용과 단계별 보고 정책을 유지했다.

현재 상태: **6단계 CLI 구현·자동/실제 통합 검증을 완료했다.** 최종 솔루션 build 경고/오류0, Core143/Media Infrastructure143/Downloader835개, 총1121개 실패/skip0이다. 직접 재생기로 화면/소리를 평가한 검증이 남아 Phase 1 전체 Passed/Completion/Tag는 확정하지 않는다. 기존 제한된 [4단계 지원 범위](PHASE_1_STAGE_4_VERIFICATION.md)와 [5단계 Application 계약](PHASE_1_STAGE_5_VERIFICATION.md)을 유지한다.

## 호스트와 계약

기존 호스트는 WPF뿐이고 CLI는 없었다. 채택한 설계대로 `apps/ClipDownloader.Cli`에 별도 net10.0 호스트를 추가했다. WPF 참조·다운로드 UI·ClipEditor는 추가하지 않았다. DI 10.0.12, Serilog 4.4.0/File sink 7.0.0의 기존 버전을 재사용하고 새 패키지·도구 설치 없이 로컬 NuGet 캐시로 자산 파일을 생성했다. 최초 sandbox restore가 자산 파일을 생성하지 못한 실행은 성공으로 기록하지 않는다. `--source C:\Users\shiro\.nuget\packages -p:NuGetAudit=false`의 실제 복원이 성공했다.

`diagnose`/`info`/`clip`, 명시 output, 기본 best/fast, 해상도/mode, 세 도구 경로와 help를 연결했다. 무명·중복·명령 범위 밖 옵션, 잘못된 URL/시간 문법은 입력 오류다. Node.js 22+와 yt-dlp EJS의 기존 정책을 유지하며 별도 runtime 옵션·자동 설치·설정 저장·JSON 출력은 추가하지 않았다. tool 경로 생략 시 기존 호스트 tools/PATH 탐색을 사용한다.

CLI는 metadata→Application Prepare→동일 준비 요약/주의·미확인 항목→확인→Execute를 호출한다. 확인 전 출력 경로를 읽기 검사하며 workspace·최종 파일·source 구간 생성은 없다. 준비 요약은 source/제목/업로더/길이·선택 포맷/해상도/FPS/코덱/음성 선언·IN/OUT/길이/mode/output/budget을 표시한다. 미확인 FPS에서 길이를 추정하지 않는다. 제한된 기존 실제 검증의 source 획득 budget **32 MiB/30초**를 고정해 표시하며 Accurate의 source-start 참조도 포함한다. CLI 조합의 초기 상한이며 임의 전체 영상 지원을 의미하지 않는다.

확인 질문은 stderr의 `위 설정으로 클립을 생성하시겠습니까? [y/N]`이다. trim 후 Y/y만 진행, N/n·빈 입력·공백·EOF는130, 다른 답은 재질문한다. redirected stdin에서 --yes 없으면 확인 입력 없이2다. --yes는 같은 요약을 표시하고 입력 대기만 생략한다. 입력/실행 검사와 최종 검증을 우회하지 않는다.

Ctrl+C는 token을 취소하며 Application/ProcessRunner가 종료·파일 정리를 완료한 뒤130을 반환한다. Finalizing 이후 늦은 취소는 확정 결과를 따른다. 반복 Ctrl+C로 정리를 생략하지 않는다. 호스트는 structured category를 입력2/실행1로 변환하고 성공0/취소130을 반환한다. 프로그래밍 오류를 포괄 catch로 일반 취소·성공으로 숨기지 않는다.

정상 정보/요약/최종 결과는 stdout, 질문/단계/경고/오류/잔존 경로는 stderr다. redirected stderr는 단계 전환만 기록하고 terminal은 같은 줄을 갱신한다. 결과는 실제 video/audio 표시 범위를 사용자 좌표의 정확한 유리수 초 `[Start, End)`로 표시한다. 최초 자동 테스트에서 record의 기본 ToString이 노출되는 문제를 발견해 유리수 출력으로 수정했다. stage100%를 전체 성공으로 판단하지 않으며 최종 ClipJobResult만 종료 코드를 결정한다.

source owner는 singleton 하나를 inspection/acquisition/resource query에 공유한다. 명시 작업 cleanup이 끝나고 DI가 해당 owner를 dispose한 뒤 logger를 flush/dispose한다. `%LOCALAPPDATA%\ClipMaker\ClipDownloader\Logs`에 별도 CLI rolling 로그를 쓰며 명령 이름·Job ID·stage·종료 코드만 기록한다. raw JSON/도구 인자/페이지 URL·signed URL/인증값을 복제하지 않는다. 로그 초기화/실제 sink SelfLog 문제는 warning flag로 보존하며 원래 작업 결과를 바꾸지 않는다. 디렉터리 주입은 내부 로깅 테스트용이며 CLI 옵션을 추가하지 않는다.

사용 방법은 [CLI README](../../../apps/ClipDownloader.Cli/README.md)를 참조한다.

## 자동 검증

기존 Downloader Infrastructure 테스트 프로젝트에 CLI 참조를 추가했다. 새로운 test 패키지·프로젝트는 없다. 독립 CLI 테스트25개를 실제 통과했다. 옵션/기본값/경로·Y/N/빈 입력/EOF/재질문·redirected stdin·--yes 검증 유지·확인 중 취소·structured code·정리 warning/잔존 path·실제 표시 범위·redirected/terminal progress·DI 조합을 검사한다. fake Execute는 호스트 동작만 검사하며 실제 clip 성공 증거로 세지 않는다.

후속으로 explicit resolution/mode, 확정 성공 이후 token 취소, 로깅 초기화 실패와 제어된 SelfLog 알림/flush를 검사하는3개 테스트를 추가했다. 최종 CLI28개를 포함한 Downloader835개가 통과했다. 실제 파일을 디렉터리로 사용할 때 초기화 실패와 사용자 파일 보존을 검사했으며, SelfLog 테스트를 실제 disk-full 환경 검증으로 확대 해석하지 않는다.

## 실제 CLI·출력 검증

제품 코드 `52f07cf`의 actual dotnet CLI를 별도 프로세스로 실행했다. 기존 yt-dlp 2026.08.19/FFmpeg·FFprobe8.1/Node22를 사용했다. ignored `RunStage6Cli.ps1`이 stdout/stderr를 동시에 drain하고 실제 숫자 종료 코드를 저장했다. stdin은 리디렉션으로 닫았으며 네 실제 clip은 명시 --yes로 실행했다. 지정된 두 URL의10~20초 요청만 획득했고 기존 사용자 파일을 덮어쓰지 않았다.

[안전한 구조화 증거](evidence/PHASE_1_STAGE_6_CLI.json)에15개 실행 결과를 기록했다. help/정상 diagnose/양 사이트 info와 네 clip은0, invalid URL·--yes invalid time·redirected stdin의 확인 거부는2, 세 도구 누락과 node를 yt-dlp로 지정한 실행 실패는1이었다. 확인 거부의 unconfirmed.mp4는 생성되지 않았다. native 취소130은 별도 아래 두 실행으로 검증했다.

| CLI 작업 | 최종 bytes | SHA256 |
|---|---:|---|
| CHZZK Fast | 11281027 | `360d6fba66306f1427f6220f076ea8f9ec7294e459bb2cd0b76ff87bcc09cdfe` |
| CHZZK Accurate | 1173183 | `3916abad8217623b35079f2502c185444cac487b56107cb0fd9aaa6714612dde` |
| YouTube137/140 Fast | 3469926 | `52314b66092c858dd4521d8e372a5546d2bc4da5e381eea87a16d0fd7d98ddd8` |
| YouTube137/140 Accurate | 4897957 | `c599d0e9beda14a895ac0003f0607aa21b0fa010385f325452742707867801ce` |

네 final은 ignored `.artifacts/stage6-real/aaa817ac5abc4d11a3a9b84dc928735f`에 보존한다. 최종 hash를 재대조했고 FFmpeg `-nostdin -v error -xerror -i FILE -map 0:v:0 -map 0:a:0 -f null -`로 모든 영상/음성을 끝까지 디코딩하여 각각 exit0을 확인했다. 기존4/5단계 packet/picture/sample/native gate를 실제 CLI에서도 통과한 결과다. 재생기로 화면/소리를 직접 평가한 증거와 구별한다.

CHZZK Fast의 실제 사용자 영상 범위는 `[86413/9600,192013/9600)`, 음성 `[86413/9600,320171/16000)`이며 요청 `[10,20)`과 다름을 표시했다. CHZZK Accurate는 기존 영상 `[96013/9600,192013/9600)`, 음성 `[10,20)`, zero2/overlap1/skip1024/tail256을 유지한다. YouTube Fast의 시작은6초로 표시하며 Accurate의 영상/음성은 `[10,20)`이다. 모든 SourceEndVerified=false를 유지했다.

## 실제 Ctrl+C와 자원 정리

ignored `RunStage6Cancellation.ps1`은 테스트가 만든 CLI만 별도 숨김 콘솔에서 실행하고 그 콘솔에 attach하여 Windows CTRL_C_EVENT를 전달했다. 다른 사용자 콘솔·프로세스에 신호를 보내지 않았다. 제품에 자동 timeout·강제 종료 정책을 추가하지 않았으며 관찰/종료 대기의 시간 제한은 검증 도구만의 bound다.

- Downloading: Job `ece23fc0-7530-49a2-9af6-ae1c66a5d002`의 원본 소유 파일3개가 존재하고 InputInspection 진입 전인 시점에 Ctrl+C를 전달했다. exit130, final 없음, known job folder0을 확인했다.
- Processing: Job `b20b22da-a0ae-4311-901d-254252cc08df`의 Processing stage에서 실제 owned FFmpeg PID3180을 관찰한 뒤 Ctrl+C를 전달했다. exit130, 해당 FFmpeg 잔존0, final 없음, known job folder0을 확인했다.

최초 controller 실행은 live stderr의 파일 공유 모드 오류로 중단됐고, 두 번째는 GetProcessById의 handle을 미리 유지하지 않아 종료 코드가 null이었다. 이 둘은 취소 성공 사례로 세지 않는다. 최초 작업은 실제 완료된 final을 남겨 보존했고, 두 번째 CLI는 취소·정리 안내를 반환했지만 숫자 코드 증거로 사용하지 않았다. 공유 read/write reader·종료 전 process handle 유지와 관찰/예외 시 취소 처리를 보완한 위 두 재실행만 통과로 기록한다. 제품 취소 코드 수정으로 오인하지 않는다.

실제 native 실행 환경의 root `C:\Users\shiro\AppData\Local\ClipMaker\ClipDownloader\jobs`가 존재함을 요구하고, 이번 통제된 CLI 로그의 Job ID8개(네 정상 작업·두 폐기된 controller 실행·두 유효 취소 실행)별 폴더0을 읽기 전용으로 재검사했다. 새 source owner를 만들어 registry를 추정하거나 sandbox app-data 경로를 실제 root로 사용하지 않았다. 임의 폴더/사용자 파일 삭제와 재귀 cleanup은 없다.

## WPF 회귀·최종 통합·완료 판정

기존 WPF binary를 Start-Process -WindowStyle Hidden으로 실행했다. 검증이 생성한 PID17500의 ClipDownloader main window를 관찰하고 CloseMainWindow로 정상 종료한 exit0을 확인했다. 기존 WPF 파일 존재 진단을 실행/버전 진단으로 변경하지 않았다. 이번 smoke는 기본 창 생성/정상 종료의 회귀이며 Phase0 전체 UI/수명 matrix를 다시 수행했다는 뜻은 아니다.

최종 `dotnet build ClipMaker.slnx --no-restore`는 경고/오류0이다. 이후 각 기존 test 프로젝트를 `dotnet test --no-restore --no-build`로 실행하여 Core143/Media Infrastructure143/Downloader835개, 총1121개 실패/skip0을 확인했다. 실제 native 작업이 로드한 DLL을 덮어쓰지 않도록 모든 작업이 끝난 뒤 build하고, 같은 빌드 assembly로 회귀 검증했다. 기존 도구 탐색 테스트도 전체 suite에 포함한다. WPF smoke 뒤 변경된 제품 파일은 CLI의 내부 로깅 테스트 디렉터리 주입뿐이며 기본 CLI 동작과 WPF binary는 유지된다.

완료 인계: 실행 파일/옵션은 위 README, 준비/실행 계약은5단계, media 전략·거절 조건은4단계 문서다. 남은 Phase1 완료 조건은 보존한 네 final의 실제 player 영상/음성·동기 직접 확인과 그 결과 기록이다. decode/native gate 결과를 그 평가 대신 성공으로 표시하지 않는다. 재생 확인에서 문제가 발견되면 원인과 영향을 분석해 필요한 현 단계만 보완한다. 필수 확인 전 Phase2의 시작 조건을 충족했다고 표시하지 않는다.

6단계 CLI 자동·실제 통합과 Phase1 전체 완료 판정을 구별한다. 정상 decoder 재생은 확인했으나 재생기로 직접 화면/소리를 평가한 증거는 없고,4단계의 codec/edit/clock/source-start budget/delayed audio/empty frame 지원 제한 및 NativeEnd Unknown은 유지한다. Phase1의 필수 재생 검증을 완료로 승격하지 않고 Passed/Completion/Tag를 Pending으로 보존한다. Phase2 설계·WPF 다운로드 UI·ClipEditor에 진입하지 않는다. Git의 기록 기준 `0759dc5`/실제 선행 `1ca2547` 동일 tree·다른 ancestry 설명도 유지한다.
