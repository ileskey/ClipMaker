# Phase 1 4단계 통합 검증 기록

2026-10-14, `phase/1`, Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지. **검증된 제한 전략 범위에서 4단계(4a 획득→4b 검사→4c 처리)를 완료했다.** 아래 누적 기록의 미완료 설명은 해당 작업 시점의 상태이며 마지막 완료 범위·제한·인계를 현재 기준으로 읽는다. Phase 1 전체와5/6은 미완료다. 개별 커밋 보고를 생략하고 4단계를 통합 보고하는 사용자 지시를 따른다.

## 획득 및 공유 probe

indexed/보조 HLS collector에 기존 HLS collector와 같은 trusted storage test seam을 연결했다. write/flush/close/delete 실패, 전송 중·쓰기 중·close 이후 취소를 검사했다. 불확실한 close에서는 삭제를 시도하지 않고 소유 파일을 인계하며, 실패·취소 결과의 Data는 null이다. 사용자 파일은 유지한다. 보조 collector의 CreateNew 충돌은 OutputConflict로 구별한다. 추가 lifecycle 16개를 포함한 Downloader Infrastructure 714개 통과.

`IMediaProbe`와 local request/result, immutable raw stream/frame/packet/skip 관찰을 공유 Media.Core에 구현했다. 요청에는 local snapshot/file identity, 절대경로, 기대 길이/SHA-256, Summary 또는 FramesAndPackets, 상세 요청의 유한 range가 필요하다. 상세 관찰은 소유 파일 전체를 검사하여 reorder/준비 관찰을 보존하며 요청 range로 seek하지 않는다. requested range는 native coverage 증명이 아니다.

`FfprobeMediaProbe`의 초기 전략은 local MP4, protocol=file/format=mov다. 읽기 공유 lock과 실행 전후 길이/SHA-256, 버전15초·probe60초, stdout Full 32MiB 상한과 EOF/완전성, stderr 오류, root/descendant/stream 종료를 확인한다. 종료 불확실은 취소보다 먼저 실패한다. raw 관찰은 Demux이며 missing PTS를 DTS/best effort/0으로 대체하지 않는다. declared/average rate는 cadence 증명이 아니다. native header 해석·qualification·decoder preparation 성공과 구분한다.

기존 초기 획득 parser는 공유 strict parser에 위임하되 기존 8MiB/Downloader 오류 경계를 유지한다. 공유 parser는 JSON32MiB/stream16/frame100,000/packet100,000/depth32의 상한을 갖는다. signed integer clock, audio sample 수, skip/discard와 미해석 side data를 보존한다. summary는 frame/packet 관찰을 암묵적으로 만들지 않는다.

기존 소유 로컬 파일로 실제 FFprobe8.1 공유 경로를 실행했다. 이 실험에서는 현재 local bytes를 identity로 고정했으며 원격 asset 전체 동일성이나 native 해석 성공을 주장하지 않는다.

| 입력 | bytes | frames / packets | 결과 |
|---|---:|---:|---|
| YouTube137 first indexed | 1,338,181 | 180 / 180 | raw Demux 성공 |
| YouTube140 first indexed | 162,728 | 430 / 430 | raw Demux 성공 |
| CHZZK 기존 first HLS | 2,050,886 | 213 / 213 | raw Demux 성공 |
| 기존 AAC edit 반례 | 193,792 | 535 / 536 | raw Demux 성공, PTS−1024/skip1024 보존; native/Accurate 지원 성공 아님 |

실행 harness는 ignored `.artifacts/initial-probe-real/ProbeShared.ps1`이다. 새 다운로드·설치·project 생성은 없었다. Core143/Media Infrastructure90/Downloader Infrastructure714개 모두 실패·skip0. 솔루션 build 경고·오류0. Media Infrastructure90개에는 실제 ProcessRunner fixture 회귀 검증이 포함된다.

## 남은 4단계 gate

- 4a의 제한된 획득 전략 audit는 아래 후속 기록에서 완료했다. 범용 원격 asset/version·format 지원은 선언하지 않는다.
- 4b의 제한된 shared native header/sample 해석 및 획득 snapshot 연결은 아래 후속 기록에서 완료했다. 처리 output/end의 추가 표현은 4c gate에 남긴다.
- 4c qualification, K/common shift/Fast 출력, Accurate source-start decoder 준비/PCM 재구성, staging·actual output/native/edit/end 검증.
- 양 사이트/양 모드 실제 clip 및 실패·취소 통합. 전체 Phase 1과 후속5/6은 미완료다.

Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다. 설치·범용 format 확장·선택 포맷 변경·whole-video fallback·허용 오차 추가를 하지 않는다.

## 공유 container/native 해석 연결

기존 제한형 MP4 header/fragment sample reader를 공유 Media Infrastructure로 옮기고 Downloader에는 기존 오류 계약을 유지하는 adapter를 남겼다. 같은 reader와 순수 correspondence gate를 acquisition 초기 검사와 shared probe에서 사용한다. FFprobe probe 요청의 `InspectContainer`가 true이면 같은 읽기 lock 안에서 header/sample 관찰을 만들고 raw stream의 명시적 track ID·정수 clock과 대조한다. header 성공과 nested NativeSamples 실패를 구별하여 미지원 progressive/AAC edit 반례를 성공으로 승격하지 않는다.

공유 `Mp4NativeObservationVerifier`는 관찰 파일의 sample/packet/frame count·PTS/DTS/duration·byte 위치/크기·codec 설정·sync/skip 상태를 대조한다. 단일 양수 duration/content edit(rate1, mediaTime≥0)만 해석하며 clipping/multiple/empty edit는 거절한다. HLS 무편집/단일 video 및 indexed 단일 stream 정책은 source adapter에 유지했다. 관찰된 native range는 원격 end/target coverage/decoder 준비 증명이 아니다.

기존 로컬 137/140/CHZZK 사례에서 native sample180/430/213개와 공유 correspondence 성공을 확인했다. AAC edit 반례는 raw/header 관찰을 보존하면서 NativeSampleEvidence UnsupportedInput으로 거절했다. 이전 초안 컴파일에서 남은 source 의존2곳을 제거했고, 새 테스트의 missing PTS 기대 필드1곳을 실제 의미인 NativeFramePtsMissing으로 수정했다. 최종 Core143/Media Infrastructure102/Downloader Infrastructure714개 실패·skip0이며, 다음 qualification/처리 및 4단계 전체는 미완료다.

## 획득 snapshot과 공유 검사 연결

`AcquiredMediaProbeService`는 획득 파일의 captured identity와 verified mapping을 입력으로 공유 probe/native gate를 실행한다. `AcquiredMediaInspection.Create`는 acquisition ID·file ID·경로·길이/SHA-256·관찰 stream 집합·mapping 범위를 대조한다. video declared FPS가 선택 FPS와 달라지면 InvalidMetadata/Execution으로 거절한다. probe 의존성이 다른 request를 반환해도 차단한다. 이 결과는 local probe 연결이며 processing qualification 성공이 아니다.

새 9개 테스트로 content/mapping 미확인, snapshot 교체, coverage 모순, missing PTS, 선택 FPS 변경, 종료 불확실, 취소의 Data=null을 검사했다. 양 사이트10~20초 Accurate 입력을 bounded32MiB/30초 검사 budget으로 새 획득하여 실제 shared probe/native binder 성공을 확인했다. CHZZK `hls-8384` target22,551,871bytes에서 video[2,24), audio[19187/9600,3599/150)을 관찰했고, YouTube137/140 target5,284,376/486,153bytes에서 video[0,24), audio[0,22016/735)을 관찰했다. 초기·refresh·보조 파일 소유권은 기존 registry에 유지한다. source-start prefix 존재만으로 decoder preparation을 확정하지 않았으며 final clip은 아직 만들지 않았다.

실제 harness는 ignored `AcquireAndProbeHls.ps1` / `AcquireAndProbeIndexed.ps1`이고 각 소유 작업폴더에 safe `shared-probe-summary.json`을 남겼다. Source NativeEnd는 Unknown을 유지한다. 제한된 4b 경로는 연결됐으며 다음은 4a 최종 audit와 4c qualification/처리다.

이번 변경 후 Downloader Infrastructure723개 실패·skip0, 솔루션 build 경고·오류0을 확인했다.

## 제한된 4a audit

단일 연속 VOD/fMP4 HLS 및 YouTube137/140 direct init/SIDX 전략을 대상으로 선택·source inspection 연결, 초기/target byte identity, cumulatively bounded 읽기, exact HTTP range/total length, native clock/config/edit, 필요한 mapping envelope, CreateNew 소유권·취소·종료 인계를 검토했다. acquisition Data는 target 파일만 포함하고, 초기·refresh·보조 및 실패 잔존 파일은 job resource registry에서 조회한다. cleanup/최종 이동의 host 통합은 후속5/6의 책임으로 남긴다.

확인된 일반 FPS가 바뀔 때 HLS/indexed inspection이 VerificationFailed를 반환하던 분류를 채택 계약대로 InvalidMetadata/Execution으로 수정하고 assertion을 추가했다. Downloader Infrastructure723개 실패·skip0을 재확인했다.

이 범위의 4a 획득과 4b local/native 연결 구현·검증은 완료했다. encrypted/discontinuous/live/master leaf, 무시된 range·짧은 body·budget 초과, multi/empty/clipping edit, 미지원 AAC 설정과 codec/layout은 명시 거절한다. 선택 변경·Best 재선택·whole-video fallback은 없다. 원격 파일 전체 content/version·source global end·decoder preparation을 획득 성공으로 추정하지 않는다. **남은 4단계는 4c qualification/처리/actual output 검증이며 전체4 완료가 아니다.**

## 4c qualification과 scoped 의존 조건

공유 input/stream 처리 계획·required video frame 기대값·exact audio lattice/ceil boundary·decoder preparation 입력 계약과 Application의 순수 qualifier를 구현했다. origin/공통축/audio presence, 필요한 mapping scope, codec/pixel 표현, K/공통 이동량, video `[IN,OUT)` membership, empty/필수 audio support와 movie clock 상한을 검사한다. output 표시 끝은 선택 frame의 duration과 구별하여 전달한다. AAC grid에 표현 불가능한 Fast 이동은 UnsupportedFormat으로 거절한다. 관찰 파일 끝을 원본 유효 audio 끝이라고 추정하지 않으며, 요청이 관찰 support를 넘으면 AudioSupportEndUnverified다.

Accurate는 같은 audio/acquisition scope의 Verified decoder preparation, 초기 source 파일 identity와 실제 획득 source-start prefix 연결, 실행한 lossless PCM의 sample count/byte identity를 요구한다. 이 입력이 없으면 DecoderPreparationUnverified/Identity로 차단한다. **준비 계약의 순수 synthetic test 성공은 production decoder 준비 성공이 아니다.** PCM 생산·재구성·encode/output 검증은 아직 남았다.

`ClipRandomAccessInspector`는 captured 파일을 read lock/hash로 재확인하고, 확인된 선행 K packet의 AVCC payload에서 모든 VCL slice가 IDR인지 검사한다. K flag만으로 닫힌 random access라고 선언하지 않으며 mixed/non-IDR, 변경된 파일은 거절한다. 실제 CHZZK10~20초 후보의 K=11/clock48000/필수600 frames, YouTube137/140 후보의 K=6/clock11289600/필수300 frames와 양 stream 공통−K 이동을 확인했다. final MP4는 아직 생성하지 않았다.

이 연결 audit에서 K가 target 앞에 있을 때 기존 B scope가 target에만 한정돼 있던 문제를 발견했다. HLS/indexed acquisition의 B scope를 실제 전체 관찰 native 범위로 수정하고, target coverage scope는 요청 범위에 유지했다. qualifier는 target와 K에 필요한 scope를 별도로 검사한다. source-start가 확인된 leading empty 구간은 byte prefix 대응이 있어야 binder가 허용한다. 이 scope 보완 후 양 사이트 현재 경로의 actual end-to-end 검증은 처리 단위에서 다시 수행한다.

`SourceStartPrefixInspector`는 같은 선택 provenance의 초기 참조와 획득 파일을 실행 전후 길이/SHA-256으로 고정하고, 실제 획득 prefix를 hash하여 초기 참조 전체 bytes와 대조한다. 이 사실은 source-start bytes 대응이며 decoder 상태·PCM 값·범용 준비 길이의 증명이 아니다. 변경/누락/취소에서는 null Data이며 기존 파일을 유지한다.

현재 Downloader Infrastructure744개 실패·skip0. 초기 clock test에서 source 이동이 상쇄되어 실제로 표현 가능한 사례를 잘못 실패 기대한 fixture를 수정했고, 이후 target/K scope·prefix 누락 거절을 추가했다. Fast 실제 mux/native/end와 Accurate decoder/PCM/encode/output gate, 처리 취소/소유권과 숫자 binder 검증은 남았다.

## Fast copy/mux backend 연결

공유 processing request/result와 source URL·format ID 없는 FFmpeg backend를 연결했다. 현재 backend는 Fast H.264/AAC 단일 local movie 축만 지원한다. 분리 stop/translation 축, native audio grid 비정렬, 음수/미표현 seek, 잘못된 frame 기대값과 exact-double 정수 범위를 넘는 `setts` 표현을 거절한다. source packet/PTS/DTS/duration의 관찰 extrema를 계획에 보존하여 rescale 전후와 translation 후 모두 검사한다. Fast stop은 AV_TIME_BASE의 ceil로 명시하고 실제 packet coverage/end 검증을 후속 gate에 요구한다. Accurate backend는 아직 미구현이며 자동 전환하지 않는다.

입력은 처리 전후 captured 길이/SHA-256과 read-only share로 고정한다. staging은 CreateNew로 소유권을 확보하고 no-delete reader를 연속 유지한 채 FFmpeg의 `-y`를 이번 소유 파일에만 사용한다. 기존 staging 충돌에서는 사용자 파일을 덮어쓰지 않는다. 버전15초, 실제 처리 고정 timeout 없음+취소를 사용한다. 실패·취소는 null Data이며 소유 staging과 종료 불확실을 resource registry로 조회한다. 파일을 자동 삭제/최종 이동하지 않는다. output hash 성공은 최종 clip 성공이 아니다.

12개 backend 테스트로 captured 입력 변경, staging 충돌, 취소/늦은 취소, 종료 불확실 우선·재사용 차단, process/stderr 실패, Drain stdout, exact-double 한계를 검사했다. Media Infrastructure114개·Downloader744개 실패·skip0. 솔루션 build 경고·오류0.

새 원격 획득 없이 기존 YouTube137/140 소유 파일을 실제 backend로 copy/mux했다. staging3,469,926bytes, SHA-256 `52314b66092c858dd4521d8e372a5546d2bc4da5e381eea87a16d0fd7d98ddd8`, raw probe/decode1025 frames/packets가 통과했다. video clock1/11289600, audio1/44100, H.264/AAC LC 설정을 확인했다. ignored `processing-real/CopyLocal.ps1`과 소유 폴더 summary를 유지한다. **progressive sample/edit/end 및 source 대응 검증 전이므로 최종 출력 통과로 선언하지 않는다.** 다음은 독립적인 MP4 output 해석/verification과 양 사이트 현재 경로 통합이다.

## Progressive MP4 output 관찰

source와 output probe 목적을 분리하고, output의 progressive stsz/stts/ctts/stsc/stco/co64/stss 및 실제 mdat 범위를 해석했다. sample 수·크기·물리 위치·signed composition·sync를 보존하고 중복/누락/범위 모순을 거절한다. source 전략의 제한을 넓히지 않는다. output AAC 설정에서는 명시적인 disabled SBR extension만 추가 해석한다.

`Mp4OutputObservationVerifier`는 native sample/packet/frame, mvhd/tkhd/mdhd, 제한된 empty+content edit, AAC skip/decoded tail을 대조한다. mdhd의 coded presentation span과 DTS duration 합을 구별한다. [FFmpeg8.1 movenc.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavformat/movenc.c)의 계산과 실제 header/sample/decode를 함께 확인했다. 이 결과는 source 대응·최종 clip 성공을 뜻하지 않는다.

위 기존 YouTube staging의 output consistency가 성공했다. video valid 범위[0,211/15), audio[0,154522/11025), 첫 AAC skip408/tail padding0을 독립적으로 확인했다. video mdhd158807040과 stts 합158430720의 차이를 오류로 오인하지 않는다. Media Infrastructure128개 실패·skip0. 다음 source payload·시간·decoded 영상 대응과 Accurate 준비/처리는 미완료다.

## Fast source/output 대응

`FastClipVerificationService`는 qualified plan/processing identity와 output 목적 probe를 연결하고, input/staging의 전후 captured hash/read lock을 유지한다. 동일 codec initialization, 정확한 source PTS/DTS, 실제 packet byte payload, 필수 frame/audio coverage와 decoded 영상 hash/크기/선택 해상도를 대조한다. 결과는 검증된 소유 staging과 실제 source 표시 범위이며 host의 최종 파일 확정은 아니다. probe/decode 종료 미확인을 operation ID로 조회하고 이후 검증을 차단한다. 실패·취소·읽기 close 실패에서 success Data를 반환하지 않는다.

CHZZK 첫 통합 실패 `CopyPacketChanged`를 조사했다. source AAC duration1024인 3개 packet에서 output1025/1023/1025가 관찰됐으며 각각 다음 source DTS와의 정확한 차이다. MP4 STTS의 DTS 간격과 AAC decoded1024 support를 분리했다. [FFmpeg8.1 movenc.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavformat/movenc.c)의 get_cluster_duration/stts 동작과 실제 bytes를 대조했다. audio 내부 sample은 정확한 연속 source packet의 다음 DTS 차이, 마지막 sample은 source duration으로 검사한다. 임의 1sample tolerance·PTS 이동·값 보정으로 허용하지 않는다. 원본 AAC payload와 decoded sample support, output edit/end 검사는 별도 유지한다.

현재 bounded10~20초 양 사이트 Fast 통합이 성공했다. CHZZK source video[11,22), audio[11,16507/750), staging11281027bytes/SHA256 `360d6fba66306f1427f6220f076ea8f9ec7294e459bb2cd0b76ff87bcc09cdfe`; YouTube137/140 source video[6,301/15), audio[6,220672/11025), staging3469926bytes/SHA256 `52314b66092c858dd4521d8e372a5546d2bc4da5e381eea87a16d0fd7d98ddd8`다. required600/300 frames와 decoded source/output 그림을 대조했다. 선택 모드/화질 fallback은 없다. 실제 파일은 ignored 작업 폴더 `0c9e2d85008b4e1aaf7d4c2132210168` / `8cd1e42c4025407aa0acd66e2d43c13c`와 safe summary로 보존한다.

Fast 추가14개 포함 Downloader758개 실패·skip0. 실제 실험이 로드한 DLL과 동시 빌드가 충돌한 실행은 실패로 기록하며, 해당 실험 종료 후 솔루션 build 경고·오류0을 확인했다. **4c-2의 제한된 양 사이트 Fast 연결은 검증했으며 Accurate4c-3과 전체4단계는 미완료다.**

## Accurate lossless sample-grid 재구성

source-order decoded PCM offset과 native lattice 위치를 구별하는 `AudioPcmReconstructionPlan`/reconstructor 및 소유 render service를 연결했다. 원본 sample support와 requested ceil 교집합만 쓰고, 확인된 내부 gap은0, 중복은 앞선 값으로 재구성한다. requested Qa는 그대로 유지한다. PCM 길이·전후 hash를 고정하고 CreateNew 충돌/변경/취소/close 실패에서 Data를 성공으로 반환하지 않는다. 생성 파일은 operation별 registry에 보존하며 삭제·최종 확정을 하지 않는다.

독립 occupancy array oracle로 mono/stereo·비정렬 원점·부분 support·내부 gap/overlap의 값을 대조했다. 재구성4개와 소유 render4개 테스트를 통과했다. 현재 작업 중인 실제 source-start decode 경로에서 CHZZK10~20초 PCM480000 samples/3840000bytes를 생성했다. 독립 sample별 first-wins oracle와 byte 동일하며 SHA256 `93e14977216995d596762f7e59dbf507bd403be4fb7723cee9adc53c8e6ea7f3`, 삽입0 sample2개/중복 제거1개다. ignored `CheckPcmOracle.ps1`과 `550217234873477aa6de6ee93804efb4/pcm-oracle-summary.json`을 보존한다.

이 값 검증은 lossless preencode 재구성이며 lossy AAC 출력 PCM byte 동일성 보증이 아니다. 실제 준비 producer/encode/output 연결은 다음 논리 단위에서 검증·기록한다. **Accurate 및 전체4단계 완료가 아니다.**

## 실제 source-start 준비와 Accurate encode

`SourceAudioPreparationService`는 Accurate의 같은 선택/source-start prefix를 실행 전후 재검사하고, captured AAC LC 입력의 처음부터 실제 f32le decode를 수행한다. seek/preroll 길이 추정 없이 실행한 source-order 값, 관찰 frame의 총 sample 수, PCM 길이/SHA-256을 결합한 준비 fact를 만든다. 초기 bytes의 존재만으로 Verified를 만들지 않는다. 실제 decoder process가 실패하거나 종료/출력/identity를 확인하지 못하면 null Data와 소유 자원을 인계한다. 처리 backend의 CreateNew/hash/취소/종료 registry를 decode와 encode에 공통 적용했다.

양 사이트 source-start 준비와 qualifier가 성공했다. CHZZK1055744 samples/8445952bytes/SHA256 `2c416caa47f77cae3d1db4ba2224947231d604d9967496a528b20252542bb9ab`, YouTube1320960 samples/10567680bytes/SHA256 `7f4aea5d0c7d0ae78ca8203cb7a0fdcfddd6329a38915276c49c59ed6cc2a2c6`다. 임의 전역 준비 길이나 remote 전체 동일성을 보증하지 않는다.

Accurate binder는 source video를 처음부터 decode하여 exact common clock/PTS trim/translation을 적용하고, 검증된 render PCM을 AAC encode한다. libx264 medium/CRF18/yuv420p/B-frame0, AAC192k를 사용한다. source/decoded/render 입력을 처리 전후 read lock/hash로 고정한다. 숫자 표현·frame membership 모순·미준비 PCM·Fast에 잘못 전달된 render를 거절한다. audio 첫 support가 requested ceil보다 늦으면 `AccurateDelayedAudioEditUnsupported`다. Qa 변경·support 밖 silence·자동 Fast 전환으로 숨기지 않으며 향후 별도 edit 전략이 필요하다.

CHZZK 첫 encode의 16/17ms는 실제 source native 시간부터 존재하며 60fps 선언만으로 CFR이라고 추정하지 않았다. 첫 B-frame 후보에서 output PTS600개는 같지만 decoded frame duration300개가 달랐다. B-frame0 후보에서600개 PTS/duration 모두 exact 대응했고 native 표시 끝이 일치했다. AAC의 완전한 첫 priming packet에 나타난 discard flag는 native edit/skip1024가 함께 일치할 때만 해석한다. audible packet discard는 계속 거절한다.

실제 native consistency: CHZZK video[13/9600,96013/9600), audio[0,10), AAC skip1024/tail256. YouTube video/audio[0,10), skip1024/tail344. source-start decode/encode4개와 binder5개, output discard2개, 준비 거절3개를 추가했으며 Media Infrastructure143개/Downloader771개 실패·skip0이다. 이 단위의 결과는 captured encode이며 최종 source/output 검증은 아래 연결 기록에서 구별한다.

## Accurate 최종 source/output 검증

`AccurateClipVerificationService`는 동일 qualified plan/processing/render identity와 input/decoded/render/staging 전후 hash를 확인한다. output native consistency에 더해 정확한 source `[IN,OUT)` frame 집합의 상대 PTS/duration, decoded frame 수·해상도, sample-grid 유효 시작/끝과 AAC skip/tail을 대조한다. video는 S_IN, audio는 requested ceil Qa를 사용하여 실제 source 표시 범위를 반환한다. lossless render 값 검증과 lossy 출력의 temporal 검증을 구별한다. probe/decode 종료 불확실은 조회·후속 차단하고 실패·취소 결과는 Data=null이다.

bounded10~20초 양 사이트 실제 Accurate 통합이 성공했다. CHZZK600 frames, source video[12,22), audio[115187/9600,211187/9600), staging1173183bytes/SHA256 `3916abad8217623b35079f2502c185444cac487b56107cb0fd9aaa6714612dde`. YouTube300 frames, source video/audio[10,20), staging4897957bytes/SHA256 `c599d0e9beda14a895ac0003f0607aa21b0fa010385f325452742707867801ce`. CHZZK 원점 O=19187/9600이므로 source 좌표12~22와 사용자10~20의 차이를 숨기지 않는다. 요청 OUT과 마지막 video frame의 실제 표시 끝은 별도로 전달한다.

실제 verified 폴더는 `0df2a23acbfc4ba9af43f918fcffc122` / `ed3a486756ce4c598822492665b47115`이며 ignored `VerifyAccurateHls.ps1` / `VerifyAccurateIndexed.ps1`과 safe `accurate-output-summary.json`을 보존한다. YouTube render PCM441000 samples/3528000bytes/SHA256 `e972fcbfffef5c82e016d96cf67aff3fe36dc97dd28099b8a2b40a72fa82eea5`도 독립 occupancy oracle와 byte 동일하다. 새 final user 파일 확정·삭제·덮어쓰기는 수행하지 않았다.

추가6개 테스트로 source membership 모순, 선택 해상도 변경, PCM byte 변경, 종료 불확실과 취소를 검사했다. shift fixture의 stream start_pts가 edit와 모순돼 예상보다 앞선 native gate에서 거절된 테스트를 수정한 뒤 모두 통과했다. Core143/Media Infrastructure143/Downloader771개, 총1057개 실패·skip0이며 솔루션 build 경고·오류0이다. `--no-build` 테스트와 실제 harness는 같은 DLL을 변경하는 빌드와 분리했다.

## 4단계 완료 범위와 5단계 인계

| 영역 | 완료한 범위 |
|---|---|
| 4a 획득 | 단일 연속 완료 VOD/fMP4 HLS, YouTube137/140 direct init/SIDX 및 제한된 provider/native bridge; bounded bytes·whole references·소유권·선택/시간축 검사 |
| 4b 검사 | captured input identity와 immutable raw/container/native 연결; declared FPS와 cadence 구별; 필요한 mapping/coverage/skip/layout 모순 거절 |
| 4c 처리 | scoped qualifier/K, Fast copy/mux 및 exact packet/picture 대응; 실제 source-start AAC 준비·lossless PCM 재구성·Accurate encode·native/decode/actual end 검사 |
| 실제 통합 | CHZZK/YouTube 각 Fast/Accurate10~20초 요청의 verified MP4 staging, PCM 독립 oracle 및 실패·취소·충돌·종료 인계 회귀 |

지원 선언은 위 전략과 확인된 입력 범위에 한정한다. encrypted/discontinuous/live/master leaf, 미지원 codec/config/edit, exact clock/숫자 표현 불가와 미확인 source relation/coverage/preparation은 명시 거절한다. Accurate source-start prefix가 획득 budget 안에 들어와야 하며 임의 preroll·전체 원격 영상 fallback은 없다. 요청 OUT 이후 audio support가 미확인이거나, 첫 available audio가 requested ceil 이후인 미검증 AAC delay edit는 거절한다. 부분 support의 lossless 값 모델 검증을 범용 codec/edit 지원으로 승격하지 않는다. zero video membership을 허용하는 별도 Fast 전략은 아직 지원하지 않는다. NativeEnd Unknown·remote 전체 content/version 미확인, lossy AAC 값 동일성 비보증, 사람의 player 재생 미검증을 유지한다.

4단계는 verified owned staging을 생산한다. app-data workspace 정책, 확인 후 실행/재검사 조정, resource registry의 통합 정리, 종료 미확인 시 파일 유지, output 폴더 staging 및 once-only 최종 확정/취소 경합은 **5단계**의 책임이다. CLI clip/Y/N/EOF/--yes·종료 코드·표시/로그/수명 통합은6단계다. 현재 CLI clip 전체 완료·사용자 지정 최종 MP4 확정·Phase 1 Completed/Passed를 선언하지 않는다. 5단계에는 자동 진입하지 않았고 Phase2/WPF 다운로드 UI/ClipEditor 변경·패키지/도구 설치·push/tag는 없었다.

완료 검증 명령: `dotnet build ClipMaker.slnx --no-restore`, 각 기존 test csproj의 `dotnet test --no-restore --no-build`. build 경고/오류0, Core143/Media Infrastructure143/Downloader771개 모두 실패·skip0. 논리 단위 자동 commit과 누적 문서를 유지하며 기존 사용자 변경·실험 파일을 삭제/초기화하지 않았다.

최종 Git 점검 보완: 기록상 Phase Base `0759dc52475538df848f765ce30c005cf755107f`는 현재 HEAD의 ancestor가 아니다. 실제 Phase 0 선행 commit은 `1ca2547`이며 두 commit의 parent는 `fc96883b18c1d6856c92ad6b029d45e095e41e69`, tree는 **동일한** `1d3d7d39a6991dbeeecb86b48d48dbaec7f716db`다. `git diff --quiet 0759dc5 1ca2547` exit0으로 소스 차이 없음을 확인했다. commit 제목은 각각 `feat: complete ...` / `feat: Complete ...`로 다르다. 두 commit의 이력이 달라진 원인을 추정 확정하지 않고, 이번 작업에서 rebase/reset/기준 hash 교체를 하지 않았다. 문서의 기준 snapshot과 실제 ancestry를 구별한다. 마지막 commit 직후 ancestor 검사의 exit1을 발견하여 이전의 ancestor 확인 문구를 바로 정정했다.
