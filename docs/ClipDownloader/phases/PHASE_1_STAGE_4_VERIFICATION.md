# Phase 1 4단계 통합 검증 기록

2026-10-14, `phase/1`, Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지. 이 문서는 누적 작업 기록이며 **4단계 완료 보고가 아니다**. 개별 커밋 보고를 생략하고 4단계 완료 후 통합 보고하는 사용자 지시를 따른다.

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
