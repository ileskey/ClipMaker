# Phase 1 4a-3c-5 bounded native sample 검증

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `9330c86384ba236b79420daef2898e48651bb534`, branch `phase/1`. 중단 시점의 source/test 변경을 보존하고 검토·보강했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **제한된 AVC/AAC-LC fragmented MP4의 sample/codec 관찰·packet/frame timing 대조 gate 구현·관련 검증 완료**. [초기 pipeline](PHASE_1_STAGE_4A3C4_INITIAL_PIPELINE_RESULT.md) 다음 단위다. public origin/FPS fact·target mapping·전체4a/처리 지원 완료가 아니다.

Mp4TimingReader의 같은 bound-file 수명에서 moov와 bounded moof bodies를 보존해 Mp4FragmentSampleReader에 전달한다. NativeSamples의 실패/미지원은 raw header/probe 관찰과 별도로 보존하며 HlsNativeReferenceVerifier에서 지원 성공으로 승격하지 않는다. packet size/pos를 추가로 읽어 실제 sample bytes와 대조한다.

- stsd 단일 avc1/avcC(version1·4-byte NAL length) 또는 mp4a/esds의2-byte AAC-LC ASC를 읽어 config size/SHA와 sample rate/channels를 보존한다. AAC는 표준 index rate·mono/stereo·GASpecific flags0·sample entry 일치만 지원한다. AVC SPS/PPS의 전체 syntax/decoder state를 자체 검증한 것은 아니다. 실제 decoder 관찰도 함께 필요하다.
- 초기 stts/stsc/stco/stsz의 빈 sample table과 mvex/trex를 요구한다. hybrid progressive/fragmented movie·다중 sample description·다른 codec 설정은 첫 전략에 포함하지 않는다. 기존 moov/moof caps에 더해 sample 수≤100,000을 적용한다.
- tfhd는 default-base-is-moof 및 지원 default fields, trun v0/v1의 duration/size/flags/composition offset을 해석한다. unsigned decode clock+signed/unsigned CTS는 BigInteger로 정확히 보존한다. 원본 외부/implicit base offset은 지원하지 않는다. byte offset은 clock으로 쓰지 않는다.
- sample bytes가 실제 mdat payload 안에 있고 서로 physical overlap이 없어야 한다. zero duration/size·길이/필수 box·default/track 모순은 거절한다. sample/end offsets는 checked arithmetic이며 데이터 payload를 별도 메모리에 복사하지 않는다.
- native gate는 동일 track/time_base/config size·codec/LC rate/channels, 모든 packet의 byte 위치/크기·PTS/DTS/duration·sync/corrupt/discard/side 상태, 모든 decoded frame의 PTS/duration 및 audio sample 수/보고된 parameters, start_pts를 대조한다. 빈/누락/모순은 VerificationFailed, edit·비zero skip/padding·다른 codec representation/Int64 범위 밖 clock은 UnsupportedFormat이다.
- `-v error` probe의 stderr TotalBytes가0이 아니면 exit0이어도 DecoderReportedError/Data=null로 차단한다. raw error 내용은 노출하지 않는다. 종료 미확인 우선순위는 유지한다.

gate 성공은 **이 참조 파일에서 검증한 무보정 native timing 관계**다. 첫 source 참조라는 provenance는 caller가 별도로 확인해야 하며 min presentation은 그때만 origin 후보로 사용할 수 있다. source/decoder 전체 history·PCM 값 동일성·CFR·전체 끝·target coverage·최종 clip 성공을 증명하지 않는다. 관찰 range는 envelope이며 gap/overlap을 제거한 연속 sample 보증이 아니다. 일반 native/media qualification은4b/4c 범위에 남는다.

관련 timing/default/CTS 해석을 [FFmpeg mov.c의 tfhd/trun 구현](https://github.com/FFmpeg/FFmpeg/blob/n8.0/libavformat/mov.c#L5219)과 대조했다. 규격/FFmpeg의 전체 format 지원을 구현했다고 주장하지 않는다.

## 실제 검증

|실행|결과|
|---|---|
|Downloader Infrastructure tests `--no-restore`|최종 exit0,546 통과/0 실패/0 skip|
|`dotnet build ClipMaker.slnx --no-restore`|exit0,경고0/오류0|
|보존된 CHZZK local fixture+실제 FFprobe/adapter/native gate|213 packets/frames/native samples·2 configs·native gate Succeeded|
|기존 G1 AAC edit oracle|raw probe Succeeded·535 frames/536 packets·skip1024를 보존, native gate UnsupportedFormat|

이전520개에서 새26개를 포함한다. default/CTS v0 unsigned·v1 signed, physical mdat/overlap·sample cap·hybrid table, missing/corrupt/changed packet/frame/clock/sync·edit/native evidence 부재·decoder 오류를 검사했다. 중단 전543개와 build 통과를 보존했고 보강 후545→546개가 통과했다. source/test 실패는 없다. 실제 harness의 internal property 직접 접근이 sample 수0처럼 보이는 문제를 reflection 접근으로 고쳐213개를 확인했다. 이는 harness projection 오류이며 제품의 native count0 성공은 아니다.

기존 ignored local fixture/oracle만 read-only 재사용했다. network·영상 다운로드·설치·새 프로젝트 생성은 없다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다. fixture range/provenance label은 remote selected/전체 coverage proof가 아니다.

## 다음과 커밋

다음은 public selected inspection의 원점/FPS/축 fact 및 실패 시 소유 resource 인계, 이어 target 참조 획득·mapping/coverage다. 범위 밖 edit/codec을 조용히 무보정으로 지원하지 않고 YouTube indexed-source의 별도 bridge/관계를 확인해야 한다. 단위별 자동 커밋을 유지하고 전체4a가 끝나기 전 완료로 표시하지 않는다.

커밋 범위: Infrastructure6·tests3·결과/설계/source 계약/baseline4문서. 메시지: `feat: Verify native timing against bounded fragment samples`.
