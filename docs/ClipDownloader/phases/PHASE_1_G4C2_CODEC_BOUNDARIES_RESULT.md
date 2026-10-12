# Phase 1 G4c-2 codec/tool·MP4 처리와 보존 경계

작성일: 2026-10-12 (Asia/Seoul). 시작 HEAD `6e595ce98224e6e911f0863d0d382de752b2e226`, branch `phase/1`, clean. G4c-1 커밋을 확인했다. Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **G4c-2 제한된 codec capability·거절 경계 대조 완료**. G4의 개별 실험 근거를 G5에 넘길 수 있지만 전체 codec/사이트 지원·계약 확정·구현 준비 완료·제품 구현/Phase 완료는 아니다. [설계](PHASE_1_DESIGN.md), [source 계약안](PHASE_1_SOURCE_TIMELINE_CONTRACT_DRAFT.md), [증거 JSON](evidence/PHASE_1_G4C2_CODEC_BOUNDARIES.json)을 연결한다.

## 1. 사전 질문과 실제 실행

기존 3c-2의 2초·640×360·30fps Matroska 입력 3개만 재사용했다: H.264 High/yuv420p + AAC LC, VP9 Profile0/yuv420p + Opus, AV1 Main/yuv420p + Opus. 음성은 모두 mono 48kHz다. 새 원격 조회/다운로드·설치·제품 변경은 없다. 임시 자료는 ignored `.artifacts/phase1-g4c2-codecs-20261012-01`에 보존한다.

계획은 실행 전에 저장했다. FFmpeg version/encoders/decoders/muxers를 조회하고 각 입력에서 MP4 stream copy와 전체 H.264/AAC 변환을 실제 실행했다. 입력 분석은 30,000,000µs/33,554,432bytes, ProcessRunner timeout은 호출당 60초다. 이 분석 수치는 실험 설정이며 제품의 고정 정책으로 채택하지 않았다.

copy는 codec 재인코딩 없이 movie timescale240000을 사용했다. encode는 libx264/veryfast/CRF18, AAC192k, passthrough, encoder/video clock90000, movie clock240000을 사용했다. encoder 존재와 실제 생성 성공을 구별하며 encode 자체는 정확한 clip 추출 시험이 아니다. source mapping·seek/trim·A/V 교정 없이 전체 파일을 변환했다.

## 2. 도구·container와 실제 값의 별도 gate

| 입력 조합 | MP4 copy/probe/decode | copy payload/영상 | H.264/AAC encode/probe/decode | 전체 clip 정확도 |
|---|---|---|---|---|
| H.264/AAC | 통과 | 모든 양 스트림 packet hash·영상60개 hash 일치 | 통과, 영상60개 | 미검증; 아래 audio 반례 |
| VP9/Opus | 통과 | 동일 | 통과, 영상60개 | 미검증; 아래 audio 반례 |
| AV1/Opus | 통과 | 동일 | 통과, 영상60개 | 미검증; 아래 audio 반례 |

현재 FFmpeg 8.1 build에서 libx264/aac와 MP4 muxer의 목록 존재를 확인했고 실제 3종 출력으로 필요한 경로를 실행했다. 이는 위 profile/pixel format/sample rate/channel에 대한 capability 증거다. 모든 profile/bit depth/channel/layout·다른 FFmpeg build·모든 player 호환성을 보증하지 않는다. AV1 input decode에 성공했다는 사실은 AV1 output encoder를 제품에 필요하다는 뜻도 아니다. Accurate output 후보는 기존 H.264/AAC다.

선택적 negative control에서 `phase1_nonexistent_encoder`를 지정했다. exit `-1129203192`, `Unknown encoder`/`Encoder not found`로 실패했고 출력은 없었다. fallback 호출도 없었다. 실제 libx264를 삭제하거나 설치 상태를 변경한 시험은 아니다. 실행 전 capability 확인과 실행 시 실패 처리를 모두 갖춰야 한다는 근거다.

## 3. 같은 packet·영상·decode 성공이어도 audio 보존은 다르다

copy 전후를 동일 float32 PCM으로 decode했다. 아래 count는 **raw decoded PCM**이며 movie edit/packet duration이 선언하는 유효 표시 끝과 구별한다.

| 입력 | source PCM samples | copy PCM samples | 첫 skip | 끝 packet discard padding | 값 대조 |
|---|---:|---:|---|---|---|
| H.264/AAC | 96256 | 96272 | 1024→1008 | 0→0 | copy의 첫16개 이후 96256개가 source 전체와 bit-exact |
| VP9/Opus | 96000 | 96624 | 312→336 | 648→관찰 side data 없음 | source 첫24개 이후95976개 = copy 처음95976개 |
| AV1/Opus | 96000 | 96624 | 동일 | 동일 | 동일 |

AAC output에는 source skip과 달리 priming16 samples가 raw PCM 앞에 나타났다. Opus output에서는 첫24 samples를 잃고 raw tail이648 samples 더 나온다. 관찰 skip/padding 차이로 raw count 변화가 정확히 설명된다: AAC `+16`, Opus `-24+648=+624`. 겹치는 sample 값은 SHA-256으로 모두 일치했다. **PCM 총수만 비교하면 시작 손실과 끝 증가를 서로 상쇄해 놓칠 수 있다.**

source coarse timestamp는 audio 1/1000, output은 1/48000이다. AAC 첫 packet `-21ms`를 그대로 옮긴 output은 `-1008/48000`·skip1008이며 source skip1024와 다르다. Opus는 source `-7ms`·skip312를 output `-336/48000`·skip336으로 표현했다. source 마지막 Opus frame은312 samples, copy는960 samples로 decode된다. source의 discard648이 output packet side data에 나타나지 않는다.

MP4 mvhd/mdhd/elst도 별도로 읽어 증거에 저장했다. movie timescale은240000, audio는48000이다. AAC audio edit의 duration481240/240000은96248 native samples 길이, Opus480240/240000은96048 samples 길이이며 raw PCM96272/96624와 다르다. raw decoder tail 전체를 유효 audio가 추가됐다는 결론으로 바꾸지 않는다. 유효 끝 판정에는 edit/packet duration·native sample model을 함께 적용해야 하며 이번 단위는 그 source end를 보완하는 새로운 remux algorithm을 만들지 않았다. 시작 skip/값 보존 문제만으로도 이 단순 copy 후보들을 정확한 보존 성공으로 승인할 수 없다.

이 반례는 3c-2의 같은 조합 copy/decode 성공을 **도구/container 성공**으로 한정한다. 기존 [G4a-2](PHASE_1_G4A2_FAST_RESULT.md)의 H.264/AAC 제한 성공이나 [G4b-1-2](PHASE_1_G4B12_OPUS_NATIVE_RESULT.md)의 native Opus 보존 후보가 잘못됐다는 뜻은 아니다. 각각 원본 clock·skip/end와 source 관계가 달랐다. 고정 +16/+24 보정·tail648 삭제를 일반 정책으로 채택하지 않는다. movie timescale만 높여도 입력에 이미 coarse한 sample phase를 복원할 수 없다.

## 4. G5에 넘길 지원·거절 경계

codec 이름의 allowlist 하나로 최종 성공을 판정하지 않는다. 현재 확인 범위는 다음과 같다.

| 처리 후보 | 확인한 근거 | 아직 필요한 조건/거절 경계 |
|---|---|---|
| Fast H.264/AAC | G4a-2의 제한된 keyframe/common shift·target coverage·edit/끝; 현재 container/payload/picture capability | native sample phase/priming/end·공통 mapping·K 표현·source coverage가 확인되어야 함. 이번 coarse MKV 단순 copy는 보존 성공 거절 |
| Fast VP9/Opus | 현재 container/payload/picture, G4b-1의 bounded parameter 확보, G4b-1-2 제한 native Opus 후보 | 확인된 profile/pix_fmt/configuration, native CodecDelay/pre-skip/end와 mapping 필요. 이번 tail-padding 입력은 기존 성공 범위 밖; 모든 WebM/Opus 지원 미확정 |
| Fast AV1/Opus | 현재 local container/payload/picture capability | 다른 VP9의 remote/source 성공을 AV1에 전이하지 않음. AV1 source mapping·초기화·native Opus/end·Fast keyframe 경로가 미확인 |
| Accurate → H.264/AAC | 위3종 whole-file encode capability, G3/G4c-1 제한 native trim/PTS 후보 | source/native sample 준비·decoder history·G3c 경계/보정·공통 shift·skip/padding/표시 끝·출력 gate 통합 필요. whole-file encode 성공을 정확한 clip 성공으로 간주하지 않음 |

Fast는 실제 시작/끝을 안내하는 모드지만 요청한 source content·A/V 관계·유효 sample을 잃어도 되는 모드는 아니다. Accurate는 재인코딩하므로 lossy 출력 PCM을 source float와 bit-exact로 요구하지 않는다. 입력 준비/보정의 sample 값 검증과 출력 codec의 priming/end·time 표현 검증을 분리한다. G3a decoder-history·G3c 계약과 기존 허용 오차 미확정 사항을 건너뛰지 않는다.

이 표는 **검증 근거와 필요한 gate의 인계**이며 전체 codec 지원 선언이나 새 public/API 구현이 아니다. Best 선택 순서·화질/모드 fallback 금지·일반 정책은 유지한다. 선택 조합의 필요한 근거가 없으면 낮은 화질/다른 codec/다른 mode로 몰래 변경하지 않는다.

G5에서 기존 오류 계약과 연결할 후보는: 실행 전 unsupported plan/필수 parameter 미확인 → `UnsupportedFormat`, 외부 도구 비정상 종료 → `ProcessFailed`, 출력 구조/sample/time 검증 실패 → `VerificationFailed`. 도구 실행 파일 부재는 `MissingTool`과 구분한다. native source 관계 미확인·capability 부족의 세부 진단/발생 단계는 G5에서 확정하며 새 오류 enum을 만들지 않았다.

## 5. 다음 단위와 검증 기록

다음 **G5a**는 G1~G4 근거를 최소 source/local descriptor·검사 범위·모드별 gate·오류 계약에 통합한다. 지원할 native/container 범위와 명시 거절할 범위를 구체화하고 구현할 수 있는 계약·검증 순서로 끊는다. 미확인 입력은 미확인으로 남긴다. G4c의 한정 실험은 기록했지만 기존 성공/반례를 모두 범용으로 해결하거나 전체 준비 완료를 선언하지 않는다. 4단계 제품 서비스는 아직 시작하지 않는다.

ProcessRunner 호출38개: 정상37개, 의도된 없는 encoder 실패1개. 모두 root/descendant 종료·stream drain/full stdout을 확인했다. 단순 copy3개의 의미 검증 실패는 process/decode 성공과 따로 기록한다. 하네스 빌드 경고0/오류0, 기존 로컬 cache source만 사용했다. packet/picture hash·PCM 공통 구간·skip/padding count·MP4 boxes와 safe evidence를 대조했다.

제품을 변경하지 않아 451개 제품 테스트를 새로 실행했다고 보고하지 않는다. ignored 원본/출력/하네스를 보존하며 signed URL·인증값을 저장하지 않는다. 논리적 커밋 대상은 결과/evidence·설계·source 계약안·baseline5개다. staging/commit은 수행하지 않는다.
