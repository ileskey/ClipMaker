# Phase 1 최종 검증 인계

2026-10-15 (Asia/Seoul), branch `phase/1`. 최종 점검 시작 HEAD `c3a6dbe8bf603aace1c5c197c04bf821fa69b212`. 이 문서는 채택한 제한 전략의 구현·검증 결과와 Git 완료점 확정을 구분한다. Phase2/WPF 다운로드 UI/ClipEditor는 진행하지 않는다.

## 사용자 실제 재생 확인

| 사이트/모드 | 사용자 확인 대상 | 결과·증거 |
|---|---|---|
| YouTube Accurate | qnjEE-wkw8k,137+140,30fps,10~40초 | [사용자 통과 보고](PHASE_1_H264_RECOVERY_POINT_SUPPORT.md#사용자-재생-확인) |
| YouTube Fast | 같은 영상/포맷/요청, out5.mp4 | [재생·음성 동기 통과](PHASE_1_FAST_FRACTIONAL_SEEK_SUPPORT.md#사용자-재생동기-확인) |
| CHZZK Fast | 14791589,hls-8384,60fps,46:10~46:40,out6.mp4 | [재생·음성 동기 확인](PHASE_1_CHZZK_FETCH_BUDGET_SUPPORT.md#후속-사용자-생성-확인) |
| CHZZK Accurate | 15537558,hls-8384,60fps,45~55초,이번보존final | [재생·음성 동기 통과](PHASE_1_CHZZK_AUDIBLE_ACCURATE_VERIFICATION.md#사용자-재생동기-확인) |

6단계의 최초 보존 네 final 전체를 사용자가 확인했다는 뜻은 아니다. 방송 시작 무음 자료의 음성 동기 확인 한계를 기록하고, 소리·움직임이 있는 새 실제 생성 자료로 해당 사이트/모드 검증을 보완했다. 생성·output/native·전체 decode와 사용자 재생 확인을 각각 구분했다. 검증 기준인 실제 양 사이트10~30초 길이의 클립 생성·정상 재생이 확보됐다. Fast의 실제 범위 차이와 Accurate의 frame/sample-grid 범위는 기존 채택 계약을 따른다.

## 기존 필수 구현·검증 근거

2026-10-15 최종솔루션 `dotnet build ClipMaker.slnx --no-restore --nologo`를재실행해경고/오류0을확인했다. 같은빌드에서각기존test프로젝트를 `dotnet test --no-build --no-restore --nologo`로실행해Core143/Media Infrastructure193/Downloader859·총1195개통과·실패/skip0을확인했다. 최종미디어검증작업이종료된뒤빌드해로드된DLL과충돌하지않았다. 마지막제품변경후WPF파일은변경되지않았으며기존6단계smoke기록을유지하고이번에는WPF창실행을재수행하지않았다.

- [4단계](PHASE_1_STAGE_4_VERIFICATION.md): 제한 원본 획득/native 대응·qualification·Fast payload/picture·Accurate 준비/PCM/output 검증.
- [5단계](PHASE_1_STAGE_5_VERIFICATION.md): 쓰기 없는Prepare·확인자료identity·Execute재검사·최종확정·소유자원정리.
- [6단계](PHASE_1_STAGE_6_VERIFICATION.md): CLI info/diagnose/clip·invalid URL/입력·도구누락/실행실패·출력/로그·실제Windows 다운로드/처리Ctrl+C130/자식종료/정리·기존WPF창생성/종료.
- 사용자후속에서 발견한 AAC skip/terminal PCM·H.264 recovery-point·Fast 분수 seek·FFmpeg decode 시간정책·획득 예산·playlist 오류표시 보완은 각각인계연결문서의실제실험/회귀근거를따른다.

## 유지할 제한과 후속 영향

| 제한/미해결 사항 | 영향과 현재 처리 |
|---|---|
| CHZZK MPD sourceURL 추출 예외 | 일부video URL은metadata부터실패한다. [진단](PHASE_1_CHZZK_MPD_METADATA_DIAGNOSIS.md),도구업데이트해결미확인·별도MPD대응필요. HLS 성공을모든CHZZK입력보장으로확대하지않는다. |
| playlist2MiB·획득64MiB/60초·계획segment64개 | [긴playlist/Accurate위치진단](PHASE_1_CHZZK_ACCURATE_PLAYLIST_DIAGNOSIS.md). 현재CLI임시기본값과별도검사상한이다. 무조건확대하거나전체원본으로fallback하지않는다. |
| Accurate source-start decode | 먼위치는명시거절한다. 검증한앞쪽구간지원과별도제한구간decode전략설계는구별한다. |
| codec/edit/clock·gap/skip/tail 지원 | 채택한관찰/바이트/sample 증거범위만지원한다. 임의tolerance·quality/mode자동변경은없다. |
| NativeEnd Unknown·remote전체identity미확인 | 출력검증성공과원본전체끝검증을구분하며SourceEndVerified=false를표시한다. lossyAAC의값동일성을보장하지않는다. |
| 중간SourceIndex 일회실패 | [recovery-point 기록](PHASE_1_H264_RECOVERY_POINT_SUPPORT.md). 원인은미확정이며이후성공을네트워크문제해결증거로표현하지않는다. |

위사항은채택한제한전략의지원경계또는별도입력확대작업으로기록한다. UI에서지원실패·실제범위·Unknown을어떻게전달할지는후속설계에영향을주지만이번작업에서Phase2로진입하지않는다. 범용사이트/임의구간/모든codec지원완료를주장하지않는다.

## Git 기준과 확정 상태

기록된Base `0759dc52475538df848f765ce30c005cf755107f`는현재HEAD의ancestor가아님을재확인했다. 실제선행 `1ca254790cb9b6daa6bd79539a5c41e91f27cad4`와parent/tree가같고diff0이지만원인미확정이다. 원래Base를수정하거나history를재작성하지않는다. 사용자미추적영상은보존한다.

이번에는필수검증결과를기록하되Completion hash·`phase-1-complete` tag를임의확정하지않는다. 완료점확정전까지Baseline의상태는In Progress(검증완료·기준점확정대기)로유지한다. 실제생성검증/사용자확인완료와공식Git완료점·후속Phase착수를구별한다.
