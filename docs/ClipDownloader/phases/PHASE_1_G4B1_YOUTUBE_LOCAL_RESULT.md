# Phase 1 G4b-1 — YouTube VP9 MP4 초기화·로컬 분리 시간축 진단

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `1cc01ed8172b0279657c58dae5e7392ccf06ece3` (`docs: Verify Fast copy source alignment and MP4 presentation ends`), branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **VP9 header 실패의 제한된 로컬 원인·보완과 분리/재병합 대조 완료**. 실제 YouTube source 원점/분리 획득 mapping·전체 G4·Fast/Accurate 지원·4단계 준비·Phase 완료는 아니다. MP4 Opus의 24-sample 제거를 새 반례로 기록한다. 제품 코드를 변경하지 않았으며 이미 채택한 일반 정책을 다시 승인받지 않는다.

## 1. 입력·사전 범위·판정

[3c-2](PHASE_1_STAGE_3C2_RESULT.md)의 기존 `yavc.mp4`(137+140), `yvp9.webm`(248+251), `yav1.webm`(399+251)와 실패한 `yvp9-copy.mp4`를 재사용했다. 당시 요청은 사용자 YouTube 영상 sFXPM_sfJNo의 [90,100)초였다. **그 획득 파일의 원본 절대 위치가 검증됐다는 뜻은 아니다.** 이번 분리 입력은 이 로컬 merged 파일을 다시 demux한 것이며 실제 원격 split format 획득과 구별한다.

도구는 기존 FFmpeg/ffprobe 8.1·SDK 10.0.302다. 새 원격 media/metadata·도구/runtime/패키지 설치·cookie import·제품 프로젝트 생성/변경은 없다. package-free harness/media/PCM/로그는 ignored `.artifacts/phase1-g4b1-youtube-local-20261011-01/`에 보존한다. ProcessRunner의 60초·root/Job 자식 종료·reader drain·Full stdout 완전성을 확인했다.

사전 질문은 (1) 실패 MP4 header 오류의 재현, (2) 같은 VP9 input의 분석 시간/byte 한도 원인 분리, (3) packet payload·PTS의 로컬 분리/재병합 대응, (4) decoded video/PCM 값과 codec skip의 별도 검사다. 기존 9.979초 video 지연을 근거 없이 보정하지 않고, 빠진 profile/pixel format을 추정해서 넣지 않는다.

원격 source를 S라고 할 때, 이번에 확인하는 기준은 **기존 획득 파일의 로컬 R**이다. `R=Lᵢ+Bᵢ`를 확인하는 것이며 `S=R+B`·원본 O·요청 coverage를 확인한 것이 아니다. 같은 숫자 timestamp가 나온다고 원격 분리 입력의 공통 축을 증명하지 않는다.

## 2. VP9 오류 재현과 입력 분석 시간

기존 실패 MP4의 ffprobe는 exit 1, decode는 -1094995529였다. `Empty VP Codec Configuration box`·`error reading header`를 재현했다. 새 default copy도 exit 0으로 파일을 만들었지만 동일한 probe/decode 실패를 보였다.

metadata-only 기본 분석은 VP9 width/height 1920×1080을 읽었지만 profile/pix_fmt는 없고 start_pts를 0으로 표시했다. stderr에는 `unspecified pixel format` 경고가 있었다. 분석을 늘리면 Profile 0·yuv420p와 실제 video 시작 9979/1000초를 확인했다. 해상도·codec 이름 또는 summary start만으로 초기화/시간축을 확정하지 않는다.

| 같은 VP9 input의 copy 조건 | 생성 exit | moov 내 vpcC box | probe / decode |
|---|---:|---:|---|
| 기본 | 0 | 8 bytes, payload 없음 | 실패 / 실패 |
| probesize 33554432만 | 0 | 8 bytes, payload 없음 | 실패 / 실패 |
| analyzeduration 30000000만 | 0 | 20 bytes, configuration 있음 | 통과 / 통과 |
| 위 두 한도 모두 | 0 | 20 bytes, configuration 있음 | 통과 / 통과 |

8-byte box는 size/type만 있고 version/flags도 없다. 20-byte box는 version/flags와 8-byte configuration을 포함한다. moov 안의 유일한 vpcC signature와 box bounds/bytes를 읽고 ffprobe의 별도 header 검사와 대조했다. 이 helper를 범용 MP4 parser로 구현하지는 않았다.

입력 video의 첫 packet/frame이 약 10초 뒤에 나타나는 이 파일에서 **byte 한도만 늘려서는 해결되지 않았고 분석 시간을 늘려 codec parameter를 확보한 경우에는 header 문제를 해결했다**. [FFmpeg n8.1 vpcc.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavformat/vpcc.c)는 pixel format에서 bit depth/chroma 정보를 얻어 VP9 configuration을 쓴다. 실패/성공의 실제 parameter와 bytes는 그 의존성과 일관된다. 이를 모든 VP9 파일의 원인·지원 보증으로 일반화하지 않는다.

30000000µs/33554432 bytes는 실험 값이다. 제품 고정 상한·무제한 분석·자동 codec 추정 정책으로 채택하지 않았다. 후속 구현은 bounded 분석과 확인된 codec parameter, 생성 후 header/probe/decode gate가 필요하다. 이번 성공은 MP4 구조/decode 가능성이고 모든 player 호환성·A/V 원본 동기 성공의 증거는 아니다.

## 3. 기존 A/V 차이는 그대로 남는다

| 기존 획득 파일 | video 첫 decoded PTS | audio 첫 decoded PTS | video frame 수 |
|---|---:|---:|---:|
| H.264/AAC | 0/15360 | 0/44100 | 301 |
| VP9/Opus | 9979/1000 | 0/1000 | 300 |
| AV1/Opus | 9979/1000 | 0/1000 | 300 |

VP9 보완 MP4도 video 9.979초·audio 0초로 시작했다. VP9와 AV1의 기존 audio는 약 20초, video는 약 10초 범위다. header 초기화 보완이 기존 시간 관계를 고치지는 않았다.

[yt-dlp 2026.08.19 external.py](https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/yt_dlp/downloader/external.py)는 선택된 각 입력에 section ss/t를 전달하고 여러 입력을 map한다. 이 코드만으로 당시 각각의 remote demuxer 기준·원본 packet 위치·왜 위 차이가 생겼는지를 확정할 수 없다. 새 remote first/target 증거가 필요하다. video를 9.979초 당기거나 두 stream STARTPTS를 독립 초기화해 원본 동기가 복원됐다고 주장하지 않는다. 이번에는 전체 source CFR도 판정하지 않았다.

## 4. 로컬 분리와 재병합 — packet 대응의 한계

VP9 입력을 copyts/avoid_negative_ts disabled로 video-only WebM와 audio-only Matroska로 분리했다. 이후 copyts로 MP4 재병합했으며, 별도 후보에서는 packet anchor로 측정한 Bᵢ를 setts에 적용했다. 측정 Bvideo/Baudio는 둘 다 0이어서 그 후보가 추가 offset을 적용했다고 주장하지 않는다.

원본 로컬 R의 video 300·audio 1000 packet이 분리 입력과 두 재병합 파일에서 모두 동일 payload와 PTS/time_base로 대응했다. 각 stream의 이동량은 0이고 누락 packet은 없었다. expanded-analysis 직접 MP4 copy도 같은 대응이었다. 원점/stream 위치를 추정 0으로 가정한 것이 아니라 packet anchor로 확인했다. video 첫 9.979초는 그대로 유지됐다.

확대 분석 직접 copy의 decoded yuv420p 영상 300개는 R의 같은 PTS에 있는 frame과 SHA-256이 모두 같았다. 이는 로컬 영상 내용 보존이다. 그러나 아래 Opus 사례처럼 packet PTS·첫 frame PTS·decode 성공만으로 음성 sample 선택까지 같다고 할 수 없다.

## 5. Opus MP4의 초기 24 samples 제거

| 단계 | 첫 audio packet PTS / time_base | skip_samples | 첫 decoded PTS | 첫 decoded samples | 총 decoded samples/channel |
|---|---|---:|---:|---:|---:|
| R WebM / 분리 Matroska | -7 / 1000 | 312 | 0 | 648 | 959688 |
| 확대 분석 MP4 / 재병합 MP4 | -336 / 48000 | 336 | 0 | 624 | 959664 |

packet PTS를 유리수 초로 비교하면 -7/1000 = -336/48000으로 같고 payload도 동일했다. 그런데 MP4의 skip은 24 samples 증가했고 decoded 첫 frame은 24개 줄었다. 전체 float PCM으로 직접 copy와 Bᵢ 적용 재병합을 대조하니, 각 출력은 **R PCM의 첫 24 stereo samples를 제거한 뒤 나머지 값과 bit 동일**했다. 시간으로는 24/48000=0.5ms다. 순수 값 차이·decoder 랜덤성·tail padding이라는 추측으로 설명하지 않는다.

이 결과는 Opus CodecDelay/pre-skip·WebM의 ms timestamp·MP4 native sample clock 관계를 후속 검증해야 하는 구체 반례다. 정확한 native phase/원점 복원은 아직 수행하지 않았다. sample 값이 줄어든 것을 임의 허용 오차로 채택하거나 audio PTS에 고정 24를 더하는 제품 보정을 넣지 않는다. R 자체의 true native presentation/sample lattice도 container/codec 근거로 확인해야 한다.

따라서 로컬 분리/재병합 packet mapping은 확인했지만 **원본 음성 sample/presentation 보존 또는 전체 Fast/Accurate 검사 통과는 미확정**이다. G4a-2 H.264/AAC 후보가 통과했던 결과를 Opus 조합에 확대하지 않는다.

## 6. 다음 단위·검증·커밋

다음 G4b-1-2는 기존 파일로 Opus CodecDelay/pre-skip·native sample grid·MP4 edit/skip 변환을 대조한다. 확인된 container/codec 의미로 보존 또는 거절 조건을 정하고, 근거 없는 sample 변경을 하지 않는다. 이후 G4b-2는 실제 YouTube의 선택 video/audio format 초기/여유 target을 제한 획득해 remote 원점·각 로컬 mapping·공통 관계·coverage를 확인한다. local R 재분리를 원격 split 검증으로 대체하지 않는다. codec/CFR/FPS·G5 통합·전체 4단계 준비/Phase 완료는 남아 있다.

34개 호출 중 정상 28개·의도한 오류 재현 6개(기존/default/byte-only 각 probe와 decode)다. copy 세 호출이 exit 0인 점도 실패 증거와 함께 보존했다. 모든 root/Job 자식 종료·reader drain을 확인했다. local runtime/hash/probe 범위 stdout은 완전했다. 기본·시간/byte 분리 보완 harness 빌드는 경고/오류 0이었다. 증거 export의 PowerShell 연산자/byte 배열 호출 오류를 수정한 뒤 독립 payload/PTS·coverage·VPCC bytes·영상 hash·PCM suffix 검증을 완료했다. 제품 전체 테스트·사람의 player 재생 확인은 수행하지 않았다.

[증거 JSON](evidence/PHASE_1_G4B1_YOUTUBE_LOCAL.json)에 기존 입력 SHA-256·계획·실행/오류 flags·안전한 명령·allowlist probe·vpcC bytes·packet indices/offset·video hash·PCM 비교를 보존했다. 새 원격 호출을 하지 않았으며 서명 URL·인증값은 없다. 기존 사용자 변경/media와 ignored 실험 자료를 보존했다.

이번 논리적 커밋 대상은 결과·증거·하위 계약안·상세 설계·baseline 5개 파일이다. staging/commit/tag/branch 변경은 하지 않았다. 권장 메시지: `docs: Diagnose YouTube VP9 initialization and Opus sample loss`.
