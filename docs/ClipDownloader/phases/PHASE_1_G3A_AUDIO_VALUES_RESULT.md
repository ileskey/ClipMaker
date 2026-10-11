# Phase 1 G3a — AAC decoded 값·sample 위치 대조

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `4a2f88d445a5438d120c248af10572e7972f616c`, branch `phase/1`, 작업 트리 clean. 선택 포맷 시간 의미와 Phase 1 Base `0759dc52475538df848f765ce30c005cf755107f`는 유지한다.

상태: **G3a 로컬 음성 값 차이 진단 완료**. [G1](PHASE_1_G1_ORIGIN_SEEK_RESULT.md)의 최대 1 LSB 차이를 float decode·S16 변환·초기 decode 상태·PNS 대조로 분리했다. G3 전체·음성 값 허용 정책·gap/overlap·AAC 출력 유효 끝·제품 정확도 보증은 아직 확정하지 않는다. 다음 논리적 단위는 G3b 끝 경계 검증이다.

## 범위·사전 기준

기존 G1의 8초 mono 48kHz `aac-edit.mp4`를 사용했다. 설치된 FFmpeg 8.1·SDK 10.0.302와 패키지 없는 ignored harness로 진행했다. 원격 미디어·새 사이트 조회·도구/패키지 설치·제품 코드/프로젝트 변경은 없다.

모든 비교 target은 source PTS **`[73200,121200)/48000 = [1.525,2.525)`**, 길이 **48000 samples**다. G1의 48 decoded chunk 제한과 달리 이번에는 실제 sample 끝을 고정했다. full decode에서 같은 target을 잘라 reference를 만들었다. 비교용 full decode는 작은 로컬 기준 입력에만 수행했다.

사전에 다음을 구분했다.

- 같은 seek 경로의 반복 결과가 같은지.
- 전체 decode 후 trim이 reference array slice와 정확히 같은지.
- S16 양자화 전에 float 값이 이미 다른지.
- 같은 float-to-S16 변환을 dither 없이 해도 차이가 남는지.
- target PTS/sample count와 값 동일성이 각각 성립하는지.
- ±1 sample 이동 비교가 차이를 설명하는지. MSE 수치를 제품 허용치로 사용하지 않는다.
- PNS on/off 대조가 실제 관찰을 구분하는지. 옵션 이름만으로 원본의 PNS 사용을 확정하지 않는다.

기존 입력의 full float decode를 같은 8초 PCM으로 잘라 AAC 128k·PNS enabled/disabled 파일 두 개를 만들었다. 이 첫 대조가 target에서 차이를 구분하지 못해, follow-up 기준을 먼저 기록하고 **동일한 4초 deterministic white noise 입력**(mono 48kHz·amplitude 0.05·seed 12345)을 한 번 생성한 뒤 AAC 64k PNS on/off로 비교했다. 재인코딩 파일들은 실험 대조군이며 제품의 이중 인코딩 경로로 채택하지 않는다.

각 입력의 full·direct seek·repeat seek·0.5초 preroll+명시 trim·full decode+trim을 비교했다. 0.5초는 이번 대조 폭이며 일반 preroll 상수가 아니다. 출력은 float32 PCM과 `aresample=dither_method=none`을 거친 signed-16 PCM으로 나눴다. 모든 FFmpeg 호출은 `-xerror`, 60초 timeout, 기존 root/descendant/reader 종료 확인을 적용했다.

## 1. 원본 G1 입력의 값 차이는 float 단계부터 존재

| 경로 | float bit가 다른 samples/48000 | 최대 float 절댓값 차이 | S16가 다른 samples | 최대 S16 차이 |
|---|---:|---:|---:|---:|
| 직접 seek | 34261 | 0.000024080276… | 1697 | 1 LSB |
| 0.5초 preroll + trim | 34278 | 0.000024855136… | 1648 | 1 LSB |
| full decode + trim | 0 | 0 | 별도 S16 변환 기준과 동일한 float | — |

직접 seek 반복은 float SHA256까지 같았다. full-trim도 full reference의 target array와 bit 단위로 같았다. float 차이는 target 시작부터 마지막 sample까지 있었고, dither 없는 S16 변환에서도 최대 1 LSB 차이가 남았다. 따라서 **S16 양자화/dither만이 원인이라는 설명은 배제**한다. 양자화가 존재하던 작은 float 차이를 정수 값 차이로 드러내는 과정과 decode 단계의 차이를 분리한다.

원본 직접 seek의 중심 구간(양 끝 제외 47998 samples) MSE는 lag 0에서 약 `4.5233×10⁻¹²`, ±1 sample에서는 약 `8.2150×10⁻⁴`였다. 같은 target PTS와 sample count를 유지했고 lag 0이 이 제한된 대조에서 가장 잘 맞았다. **구간 전체가 한 sample 앞/뒤로 이동했다는 가설을 지지하지 않는다.** 이것만으로 임의 오류나 모든 시간 대응을 배제하지 않는다.

## 2. 첫 PNS 대조의 한계·초기 decode 상태

128k 재인코딩 대조 파일은 encoded SHA256이 서로 달랐지만, 이번 target의 decode 비교 결과는 on/off가 같았다. PNS enable이 이 target에서 효과를 냈다고 단정할 수 없으므로 원본 원인 판정의 유효한 대조로 사용하지 않았다.

두 파일 모두 직접 seek에서 최초 **528 samples**의 float 값이 달랐고, 최대 차이는 약 **0.83822**, S16 최대 차이는 **27467**이었다. 그 이후 값은 full reference와 같았다. 0.5초 preroll+trim은 target 전체가 float/S16 bit 단위로 같았다.

여기서도 첫 decoded PTS는 73200, 끝은 121200, sample 수는 48000이었다. 즉 **PTS·coverage가 맞아도 초기 decoded 값이 충분히 준비되었다는 보장은 아니다.** 이번 소량 초기 차이는 제한된 preroll 대조로 사라졌으나 이 폭을 모든 codec/input에 일반화하지 않는다. source layout/index/seek가 실제로 decode한 이전 자료의 범위도 영향을 줄 수 있으며 직접 seek라는 이름만으로 그 범위가 같다고 가정하지 않는다.

## 3. noise PNS 대조에서는 지속 차이를 구분

| 4초 noise에서 자른 target | PNS on | PNS off |
|---|---|---|
| 직접 seek float 차이 | 48000 samples, target 끝까지 | 최초 528 samples |
| preroll 후 float 차이 | 48000 samples, target 끝까지 | 0 samples |
| preroll 후 최대 float 차이 | 약 0.0617733 | 0 |
| preroll 후 S16 차이 | 47950 samples, 최대 2024 LSB | 0 |
| full decode 후 trim | reference와 bit 동일 | reference와 bit 동일 |
| 같은 seek 반복 | bit 동일 | bit 동일 |

on/off는 같은 deterministic noise PCM을 같은 bitrate로 인코딩하고 PNS 설정만 바꿨다. 이번 대조는 초기 overlap 상태 차이와 preroll 후에도 남는 decode-history 차이를 구분하는 근거가 된다. 1 LSB를 보편적인 codec 차이 한도로 삼는 가정에도 반례다. noise의 원래 amplitude, PNS 합성 성질, 파형 차이를 일반 입력의 손상/허용 판정으로 그대로 옮기지 않는다.

설치 버전과 같은 FFmpeg n8.1 AAC decoder는 초기 PRNG 상태를 설정하고 NOISE_BT band를 복원할 때 상태를 갱신한다. 이전에 decode한 packet/band가 다르면 합성 값의 이력이 달라질 수 있다는 구조와 위 대조 결과가 일관된다. [AAC 초기화](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavcodec/aac/aacdec.c), [spectral noise 복원 경로](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavcodec/aac/aacdec_proc_template.c)

**원본 G1 AAC의 각 band를 직접 파싱하거나 내부 PRNG 상태를 관찰한 것은 아니다.** 원본의 지속적인 미세 차이에 PNS/history가 영향을 준다는 설명은 지지되지만, 정확한 원본 root cause를 확정한 것은 아니다. 유효했던 noise 대조와 효과가 없었던 첫 target 대조를 모두 기록한다.

## 4. 시간·값·끝의 보증을 분리

다섯 입력의 모든 부분 decode는 첫 PTS 73200·끝 PTS 121200·48000 samples를 확인했고 내부 decoded chunk 경계는 연속이었다. sample 시각/수는 같아도 값은 위와 같이 달랐다. 이번은 mono synthetic/기존 로컬 AAC에 대한 대조이며 실제 CHZZK의 stereo gap/overlap이나 모든 AAC profile을 실증하지 않는다.

진단에서 얻은 후속 설계 근거는 다음과 같다. 아직 제품의 성공 조건·허용치로 일괄 채택하지 않는다.

- lossless 기준 입력의 exact sample 대조와 AAC decode 이력에 따른 값 대조를 구별한다.
- byte 동일성을 얻기 위해 PTS를 이동하거나 codec sample 값을 임의로 바꾸지 않는다.
- 초기 decode 상태가 부족한 결과는 target packet/PTS 존재만으로 충분하다고 보지 않는다. codec preparation/preroll 근거가 필요하다.
- PNS/history 파형 차이는 짧은 preroll만으로 항상 없어지지 않는다. 전체 source 다운로드·decode를 제품 fallback으로 추가하지 않는다.
- 원점·mapping·sample presentation·A/V 동기와 파형 bit 동일성은 서로 다른 검증이다. lossy 재인코딩 이후의 byte 동일성을 frame 정확도의 정의로 사용하지 않는다.

G3b는 **알려진 sample 끝을 갖는 로컬 입력과 G2 target을 기준으로 AAC priming/padding·유효 presentation 끝·gap/overlap 처리 후보**를 대조한다. 이번 source raw full decode 수는 기존 입력/128k 대조 각각 384000, noise 대조 각각 192512였다. noise 생성 길이의 192000과 다른 tail의 의미를 packet/edit/skip 메타데이터와 확인해야 하므로 현재 512 samples를 임의 제거하거나 확정된 padding 정책으로 채택하지 않는다. G3 전체·Accurate 성공·4단계 준비는 여전히 Pending이다.

## 실제 검증·증거·커밋 범위

두 harness 빌드는 경고/오류 0, FFmpeg **45개 호출 모두 exit 0·root/descendant 종료·reader drain**을 확인했다. 전체-target slice·반복 seek·float/S16 구분·±1 sample 대조·decode PTS/sample 수를 검증했다. 과정에서 첫 PNS 대조가 원인을 구분하지 못한 한계와 직접 seek 초기 값 실패를 그대로 기록했다. numeric 허용치·음성 품질 통과 기준은 만들지 않았다.

[계획·명령·실행·float/S16 비교·decoded sample 시각·파일 hash](evidence/PHASE_1_G3A_AUDIO_VALUES.json)에 근거를 남겼다. media와 raw 로그는 ignored `.artifacts`에 보존하고 증거에는 실행 주소 대신 고정 실험 파일명을 사용했다. 제품 코드 변경이 없어 전체 제품 테스트는 반복하지 않았다.

이번 논리적 커밋 범위는 결과 문서·증거 1개·하위 계약안·상세 설계·baseline **5개 파일**이다. staging/commit·Phase 2·WPF 다운로드 UI·ClipEditor·제품 서비스 구현은 수행하지 않았다.
