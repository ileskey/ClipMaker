# Phase 1 G4b-1-2 — Opus native timestamp·pre-skip와 PCM 보존

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `a9d39aece38ae8c49d068a738c8350a2c7256286` (`docs: Diagnose YouTube VP9 initialization and Opus sample loss`), branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **제한된 로컬 Opus native 시간 복원·MP4 PCM 보존 후보 검증 완료**. [G4b-1](PHASE_1_G4B1_YOUTUBE_LOCAL_RESULT.md)의 첫 24-sample 제거 원인을 container timestamp 정밀도로 설명하고 보존 후보를 대조했다. 원격 YouTube source 관계·모든 Opus/Matroska 지원·제품 구현·전체 G4/4단계 준비/Phase 완료는 아니다. 기존 source 시간 의미와 처리 정책은 유지한다.

## 1. 사전 질문·범위

기존 `yvp9.webm`, G4b-1 분리 video/audio 파일·PCM/hash 증거를 재사용했다. 새 remote media/metadata·도구/runtime/패키지 설치·제품 코드/프로젝트 변경은 없다. FFmpeg/ffprobe 8.1·SDK 10.0.302, 기존 ProcessRunner의 60초·root/Job 자식/reader 종료 확인을 적용했다. ignored `.artifacts/phase1-g4b12-opus-native-20261012-01/`에 실험 자료를 보존한다.

사전 기준은 실제 EBML block·timestamp scale·CodecDelay·OpusHead pre-skip 대조, packet payload로 독립 대응, 모든 packet에서 native/demux 시간 차이의 일관성, native frame PTS/sample 수·PCM 값·MP4 dOps/edit/끝 검증이다. 고정 +24 보정과 임의 sample 오차를 채택하지 않는다. 0 보정·틀린 +25·다른 CodecDelay 통제 입력을 준비해 의미 검사가 잘못된 결과를 거절하는지 확인했다.

이번 기준은 기존 획득 파일의 **로컬 R의 native timeline**이다. R이 upstream 원격 source의 어느 위치인지, 최초 절단에서 source sample을 이미 잃었는지는 아직 검증하지 않았다. R의 9.979초 video 시작을 지우지 않는다.

## 2. 실제 container 의미와 demux 정밀도

EBML Info/Tracks/Cluster/SimpleBlock/BlockGroup을 읽었다. 이번 파일의 TimestampScale은 **1000000ns**, TrackTimestampScale은 1이며 lacing·content encoding·discard padding을 적용할 사례는 없었다. 원본과 분리 audio의 Opus CodecDelay는 **6500000ns**, OpusHead pre-skip은 **312 samples**였다.

[Matroska element 규격](https://www.matroska.org/technical/elements.html)은 CodecDelay를 ns로 표현하고 playback timestamp에서 차감하도록 정의한다. [Opus mapping](https://www.matroska.org/technical/codec_specs.html#a_opus)은 CodecDelay와 48kHz pre-skip의 관계를 정의한다. 따라서 이 입력에서 native packet 시각은 `(ClusterTimestamp + BlockRelativeTimestamp) × TimestampScale − CodecDelay`다.

첫 audio block의 container 시각은 0이므로 native packet PTS는 -6.5ms = -312/48000초다. 그런데 ffprobe packet은 -7/1000초, 즉 -336/48000초였다. **6.5ms delay가 1ms demux time_base에서 7 ticks가 되면서 native와 24 samples 차이**가 생겼다. 첫 packet만으로 추정하지 않고 모든 audio block 1000개의 payload와 native 시각을 대조했다. 모두 같은 잔여 24 samples였다. video block 300개의 잔여는 0이었다.

[FFmpeg n8.1 matroskadec.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavformat/matroskadec.c)는 CodecDelay를 track time_base로 환산한 뒤 block timestamp에서 차감하고, 초기 sample skip은 별도로 native rate로 계산한다. 이번 raw container·demux 값의 차이는 그 변환과 일관된다. decoded 값이 같더라도 coarse ffprobe frame PTS를 그대로 native sample grid라고 부르면 틀릴 수 있다.

## 3. native timestamp 후보와 통제 입력

검증한 후보는 모든 대응 packet에서 `residual = nativePTS − demuxPTS`를 native audio sample 단위로 계산하고, copy의 audio setts PTS/DTS에 그 값을 적용한다. 원본/분리 잔여는 +24였다. video timestamp·압축 payload·source sample 값은 바꾸지 않았다. MP4 audio time_base는 1/48000, movie/video clock은 240000을 사용했다. 이 clock·잔여 숫자는 제품 상수가 아니다.

통제 입력은 원본 WebM의 **새 사본**에서 CodecDelay=8000000ns와 pre-skip=384로 두 metadata 값을 일치시켰다. source Tracks/TrackEntry에는 변경 대상 CRC가 없어 checksum을 훼손하지 않았다. 실제 byte 차이는 4개였고 CodecDelay/OpusHead pre-skip 위치 밖의 변경은 0이었다. compressed block payload·video timestamp는 그대로였다. 이 metadata 변경은 입력의 sample 의미를 의도적으로 바꾸는 실험이며 실제 제품/사용자 원본 수정이 아니다.

통제 input의 native/demux 첫 PTS는 모두 -8ms = -384/48000초라서 모든 packet의 계산 잔여가 **0**이었다. decoded reference PCM은 원본 PCM의 첫 72 samples를 제외한 slice와 bit 동일했다. 이는 declared pre-skip 384−312의 의도한 차이며 MP4 후보는 그 통제 reference를 그대로 보존해야 한다.

## 4. PCM·native PTS·skip·edit 대조

| 후보 | 적용 잔여 samples | 첫 packet PTS (1/48000) | 첫 skip / decoded samples | 전체 samples/channel | native 의미·PCM 보존 |
|---|---:|---:|---:|---:|---|
| 원본 0 대조 | 0 | -336 | 336 / 624 | 959664 | 실패 — 앞선 반례 재현 |
| 원본 계산값 | 24 | -312 | 312 / 648 | 959688 | 통과 |
| 원본 틀린 값 | 25 | -311 | 311 / 649 | 959689 | 실패 — 1 sample 추가·native 시간 불일치 |
| 분리 입력 계산값 | 24 | -312 | 312 / 648 | 959688 | 통과 |
| 8ms 통제 계산값 | 0 | -384 | 384 / 576 | 959616 | 해당 통제 reference에 통과 |

계산값 원본/분리 출력의 전체 stereo float PCM SHA-256은 원본 reference와 같았다. 첫 24개를 잃거나 추가 생성하지 않았고 sample 값/길이를 모두 보존했다. 틀린 값은 process/decode 성공과 첫 frame PTS 0만으로 놓칠 수 있으므로 semantic gate에서 거절했다.

모든 output packet의 payload를 EBML block과 순서별 대조했다. 정상 3개 후보는 native PTS를 정확히 유지했고 모든 audio decoded frame의 PTS/sample 수가 native sample model과 일치했다. 첫 frame은 pre-skip 후 0에서 시작하며, 원본의 다음 frame은 **648/48000=13.5ms**다. coarse ffprobe R frame의 13ms를 정확한 sample 위치로 채택하지 않는다.

MP4 dOps의 pre-skip과 edit의 media time도 별도로 읽었다. 원본 계산값은 둘 다 312이며, audio edit duration은 4798440/240000초·stream duration은 959688/48000초로 PCM의 유효 끝과 일치했다. 8ms 통제의 dOps/edit는 384로 그 native 끝에 일치했다. [FFmpeg n8.1 movenc.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavformat/movenc.c)의 dOps 저장 구조와 actual bytes를 대조했다. dOps 또는 process 성공 하나만으로 검사 통과를 정하지 않는다.

원본 계산값의 decoded yuv420p 영상 300개도 R의 같은 PTS/값과 SHA-256이 모두 같았다. video 첫 시각 9.979초와 끝 19.979초는 유지했다. audio 유효 끝은 19.9935초다. 이 관계가 upstream 원본 동기와 맞는지는 이번에 판정하지 않았다. 사람의 player 재생 확인도 수행하지 않았다.

## 5. 통합 후보·한계·다음 단위

G4b-1의 sample 제거는 이 로컬 파일에서 **정확한 native CodecDelay와 coarse demux 차이를 복원하는 경로로 해결 가능**했다. 이는 source sample 값을 변형하는 gap 보정이나 arbitrary tolerance가 아니다. 적용 전에 container/codec 일치·native grid 표현·payload 대응·모든 packet의 잔여·결과 native frame/PCM/metadata를 확인해야 한다. 근거가 없으면 잔여를 추정하지 않고 그 경로를 미지원/검증 실패로 남긴다. G5에서 최소 서비스/오류 gate로 통합하며 범용 EBML 엔진을 선행 구현하자는 결론은 아니다.

이번 임시 reader는 track scale 1·비lacing·비encoding·확인된 single phase 범위에 한정하고 범위 밖 조건은 명시 거절했다. 복잡한 timestamp 변화·lacing·padding·서로 다른 pre-skip/CodecDelay·seek preroll·다른 Opus input의 보편 지원은 미확정이다. 고정 +24 규칙은 8ms 통제의 잔여 0과 충돌한다.

다음 **G4b-2**는 실제 YouTube 선택 video/audio 원본의 초기/여유 target을 제한 획득하여 원점·각 local mapping·공통 관계·coverage를 확인한다. 이미 채택한 다운로드 허용 범위와 도구/인증/재시도 정책을 유지한다. 이후 codec/CFR/FPS·G5 통합이 남아 있다. 준비 완료와 제품 구현 완료는 별도로 판단한다.

## 실제 검증·증거·커밋 지점

23개 tool 호출 모두 exit 0·root/Job 자식 종료·reader drain, probe/hash stdout 완전성을 확인했다. output 5개 중 3개는 제한된 의미 검사를 통과했고 0/틀린 잔여 대조 2개는 의미 검사에서 거절했다. 최초 harness 빌드는 미사용 helper CS8321 경고 2개가 있었고 이를 제거한 후 최종 빌드는 경고/오류 0이었다. 제품 전체 테스트는 제품 변경이 없어 반복하지 않았다.

별도 PowerShell에서 native timestamp 공식·packet payload·모든 decoded audio frame의 native PTS/sample 수·PCM 전체 hash·dOps/edit·video hash·통제 input byte 차이/PCM slice를 검증했다. [증거 JSON](evidence/PHASE_1_G4B12_OPUS_NATIVE.json)에 raw EBML timing·plan·안전한 실행 증거·output probe/atoms·독립 판정을 남겼다. 원격 URL/인증값은 없고 기존 source 증거/PCM/media SHA-256으로 참조를 연결했다.

이번 논리적 커밋 대상은 결과·증거·하위 계약안·상세 설계·baseline 5개 파일이다. staging/commit/tag/branch 변경·Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다. 권장 메시지: `docs: Verify native Opus timing and PCM preservation`.
