# Phase 1 4a-3c-3c-3b-2 packet/skip/padding 관찰

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `52763e82a0753ff7d23f9b69bdd34fddb44c1adc`, `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지. 사용자가 4a의 논리적 단위별 자동 커밋을 허용했다.

상태: **bounded packet timing·skip/padding 관찰과 동일 파일 probe 연결 구현/관련 검증 완료**. [codec 관찰](PHASE_1_STAGE_4A3C3C3B1_CODEC_RESULT.md) 다음 단위다. native/priming 관계·source O/coverage·4a 전체 완료가 아니다.

FFprobe의 stream/frame 요청에 show_packets와 선택된 packet/packet_side_data 필드를 추가했다. 실제 결합 출력은 packets_and_frames 배열이다. parser는 type으로 frame/packet을 분리하고, 이전 별도 frames/packets 배열도 읽되 두 layout을 섞으면 거절한다. 같은 local file/lock/hash·60초·종료/출력 제한을 유지하고 별도 network·retry·decode 실행은 추가하지 않는다.

- packet stream index·signed Int64 PTS/DTS·optional 비음수 duration·보고된 flags를 보존한다. PTS 부재를 DTS/0/best_effort로 채우지 않는다. packet flags만으로 native keyframe/decoder 준비를 판정하지 않는다.
- audio Skip Samples side data의 optional unsigned32 skip_samples/discard_padding, optional byte skip_reason/discard_reason을 보존한다. side data 부재와 존재하지만 필드 누락, 명시적0을 구별한다. 부재를 skip0/priming 없음으로 승격하지 않는다.
- 미해석 side data는 raw 이름/내용을 노출하지 않고 HasUninterpretedSideData로 보존한다. duplicate Skip Samples·잘못된 숫자/타입·video skip·foreign stream·중복 JSON·control flags는 InvalidMetadata/Data=null이다.
- frame≤100,000·packet≤100,000·결합≤200,000·packet side entries≤64·JSON8MiB/depth32를 적용한다. flags는 최대16 ASCII letters/underscore다. 파서 수락 상한이며 ProcessRunner Full buffer 상한을 바꾼 것은 아니다.
- frame-only legacy 응답의 PacketsObserved는false, packet 배열/결합 배열의 존재는true다. production probe는 frame-only 응답을 PacketObservation 실패로 차단한다. 빈 packet 배열은 빈 관찰이며 완전한 coverage가 아니다.

## 실제 검증

|실행|결과|
|---|---|
|Downloader Infrastructure tests `--no-restore`|최종 exit0,507 통과/0 실패/0 skip|
|`dotnet build ClipMaker.slnx --no-restore`|exit0,경고0/오류0|
|보존된 CHZZK local fixture + 실제 adapter/FFprobe|Succeeded,213 packets/213 frames,missing PTS0,Skip side data0|
|기존 G1 aac-edit.mp4 + 실제 adapter/FFprobe|Succeeded,536 packets/535 frames,첫 audio packet PTS/DTS=-1024,skip_samples1024/discard_padding0/reasons0|

새20개 사례는 mixed 배열·signed clocks·누락/unknown side·UInt32/byte 경계·중복/역할/배열·packet/side 상한과 production 연결을 검사했다. 첫 실행은 test의 raw string 구문 오류2개로 컴파일 실패했고 fixture를 수정한 최종507개가 통과했다. 추가 빌드 경고/오류는 없다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

실제 검증은 기존2,050,886bytes local fixture와193,792bytes G1 oracle를 read-only 재사용했다. ignored Probe.ps1 reflection harness를 보존했다. fixture의 range/provenance는 test 입력으로 remote selected/coverage/실제 소유 서비스 증명이 아니다. 기존 원본/복사본을 변경/삭제하지 않았다. network·영상 다운로드·설치·새 프로젝트 생성은 없다.

## 다음 단위와 커밋

다음은 codec/container sample timing·native evidence gate이며 실제 skip/edit 반례를 유지한다. 원점 후보의 stream start/decoded first와 packet skip/fragment/container 관계를 대조해야 한다. 필요한 증거가 없으면 Unknown/VerificationFailed로 남기고 verified native input을 만들지 않는다. selected inspector·CHZZK target 획득·YouTube indexed 획득·전체 통합 검증은 남아 있다.

커밋 범위: Infrastructure3·tests2·결과/설계/source 계약/baseline4문서. 자동 커밋 메시지: `feat: Preserve bounded packet timing and skip observations`. push·4b/4c·Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.
