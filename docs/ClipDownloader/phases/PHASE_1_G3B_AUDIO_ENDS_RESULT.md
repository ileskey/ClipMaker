# Phase 1 G3b — AAC 유효 끝·gap/overlap 처리 후보 대조

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `e801c56ff5c3a95abfc53bfb9899871a1269e470`, branch `phase/1`, 작업 트리 clean. 선택 포맷 시간 의미와 Phase 1 Base `0759dc52475538df848f765ce30c005cf755107f`는 유지한다.

상태: **sample-aligned 48kHz 입력의 AAC/MP4 끝·불연속·보정 후보 대조 완료**. 기본 movie clock의 끝 충돌과 AAC 재인코딩의 gap 위치 이동을 확인했다. 확인한 source sample을 연속 clock에 배치하는 후보는 lossless 기준과 일치했다. 제품 보정 규칙·임의 sample rate/비정렬 경계·A/V 표시 끝·G3 전체·4단계 준비 완료는 아직 확정하지 않는다.

## 범위·사전 기준

[G3a](PHASE_1_G3A_AUDIO_VALUES_RESULT.md)의 작은 로컬 기준 파일과 [G2](PHASE_1_G2_COVERAGE_RESULT.md)의 이미 획득한 CHZZK 두 prefix만 사용했다. 설치 FFmpeg/ffprobe 8.1·SDK 10.0.302·기존 ProcessRunner를 재사용했다. 새 원격 요청·도구/패키지 설치·제품 코드/프로젝트 변경은 없다.

실험 출력은 **audio-only AAC를 담은 MP4/M4A**다. 제품의 최종 A/V clip 검증이나 video 표시 끝 검증으로 해석하지 않는다. target 끝의 정확한 정수 sample을 기준으로 stream duration, packet duration, decoded frame, MP4 edit/mvhd/mdhd를 비교했다. 모든 호출은 60초·종료/reader 확인을 적용했다.

사전 기준은 다음과 같다.

1. 알려진 source sample 끝과 stream·packet·edit의 presentation 끝이 같은지. metadata가 충돌하면 유리한 하나를 골라 통과시키지 않는다.
2. raw decode 길이와 priming/padding 제외 후 presentation 범위를 구별한다.
3. gap/overlap은 길이 합·union 길이뿐 아니라 정확한 위치를 비교한다.
4. 보정 후보의 삽입·제거·그 밖의 값 유지가 독립적인 lossless sample 배치 모델과 맞는지.
5. numeric 허용치나 제품 보정 정책을 실험 성공만으로 채택하지 않는다.

## 1. 48001 samples와 movie clock 반례

기존 float 입력에서 정확히 48001 samples를 잘라 같은 AAC 설정으로 인코딩했다. 기본 movie timescale과 `movie_timescale=48000`만 대조했다.

| 측정 | 기본 movie clock | 48000 movie clock |
|---|---:|---:|
| 요청 samples | 48001 | 48001 |
| movie timescale | 1000 | 48000 |
| edit content duration | 1000/1000초 | 48001/48000초 |
| stream presentation end | 48000/48000초 | 48001/48000초 |
| 마지막 packet end | 48001/48000초 | 48001/48000초 |
| raw decoded samples | 48128 | 48128 |
| metadata end 일치 | **불일치** | 일치 |
| 요청 끝 일치 | **실패** | 일치 |

기본 파일은 media header에 priming을 포함한 duration 49025/48000을 갖고, movie header duration은 1001/1000이었다. 그러나 edit content는 1초이고 ffprobe stream/format도 1초로 보고했다. packet 마지막 duration은 897로 끝 48001을 유지했다. **단일 duration 문자열·raw sample 수로 정확한 출력이라고 판정할 수 없는 반례**다.

precise 파일은 movie/edit/stream과 packet의 presentation 끝이 모두 48001/48000에 맞았다. 최초 packet PTS는 -1024, skip_samples는 1024, edit mediaTime도 1024였고 첫 decoded PTS는 0이었다. 마지막 decoded frame은 PTS 47104·1024 samples지만 packet의 유효 duration은 897이므로 declared end 이후 127 samples는 coded tail에 속한다. raw decode 길이에 요청 끝을 맞추려고 유효 sample을 바꾸지 않는다.

48000 clock은 이번 sample rate에 맞춘 **실험 값**이다. 모든 FPS/sample rate의 movie/track clock을 고정하는 규칙으로 채택하지 않는다. 필요한 정수 clock의 선택·표현 한계는 후속 계약 통합에서 결정한다.

## 2. G3a noise tail 512 samples의 의미

기존 4초 noise PNS on/off 파일을 추가 probe했다. 두 파일 모두 stream start 0·duration_ts 192000, 마지막 packet PTS 191488·duration 512로 presentation end 192000에 일치했다. 마지막 decoded frame은 1024 samples여서 raw decode는 192512였다. 최초 AAC priming은 -1024/skip 1024로 구별됐다.

따라서 이 두 파일에서 raw decode의 추가 512 samples는 **확인된 presentation 끝 밖의 coded tail**이다. 모든 파일에서 512를 빼는 규칙은 아니다. stream·packet·edit 근거와 target을 확인한 다음 decoded interval을 유효 범위에 대조한다. metadata가 충돌하는 기본-clock 파일은 이 방식의 전제도 충족하지 않는다.

## 3. 개수는 같아도 gap/overlap 위치는 이동

3초 lossless 기준 입력을 만들었다. 정확히 144000 sample 값에 source clock의 1-sample gap과 1-sample overlap을 부여했다. PCM/NUT로 source PTS와 값을 유지한 뒤 AAC로 재인코딩했다.

| source 기준 | AAC timestamp 유지 결과 |
|---|---|
| gap `[48000,48001)` | gap `[48128,48129)` |
| overlap `[96000,96001)` | overlap `[96256,96257)` |
| sample 길이 합 144000 | 144000 |
| union 길이 143999 | 143999 |
| declared end 144000 | 144000 |

길이 합·union 길이·끝은 같지만 **presentation union은 다르다**. 작은 source chunk를 고정 크기 AAC frame으로 다시 묶는 경로에서 불연속의 위치가 유지되지 않았다. 소스 PTS를 filter에서 보존했다는 이유만으로 원본 sample clock의 모든 지점이 보존되었다고 주장하지 않는다.

CHZZK 두 target도 같은 반례를 보였다.

| 항목 | 1080p timestamp 유지 | 720p timestamp 유지 |
|---|---:|---:|
| 입력/출력 유효 sample 길이 합 | 479999/479999 | 480001/480001 |
| 입력/출력 union 길이 | 479998/479998 | 479999/479999 |
| 선언 presentation 끝 | 480000 | 480000 |
| 입력과 출력 union 위치 일치 | **실패** | **실패** |

예를 들어 1080p source의 gap 시작 35488은 출력 35840으로 옮겨졌다. 720p source의 overlap 경계 previousEnd 36513은 출력 36864로 옮겨졌다. 모든 위치는 [증거](evidence/PHASE_1_G3B_AUDIO_ENDS.json)에 보존한다. G2는 **획득 파일의 decoded source 대응**이 일치했다는 결과이며, 이번은 그 이후 AAC 재인코딩의 반례다. 이전 trim 실험의 sample/gap 개수 일치를 정확한 위치 보존으로 확대하지 않는다.

## 4. 연속 sample-grid 후보의 값 대조

확인된 source sample을 target의 정수 sample clock에 배치하고 빈 시각은 0, 같은 시각의 중복은 앞선 값 유지로 정의한 독립 모델과 비교했다. 실험 filter는 `aresample=async=1:first_pts=0:min_comp=0:min_hard_comp=0:max_soft_comp=0`이며 soft stretch를 추가하지 않았다. 공식 문서는 async의 fill/trim, first_pts의 시작 처리, hard/soft compensation 설정을 구분한다. [FFmpeg resampler 문서](https://ffmpeg.org/ffmpeg-resampler.html)

| 입력 | source samples | 삽입한 zero samples | 제거한 overlap samples | 결과 samples | 모델과 lossless 값 비교 |
|---|---:|---:|---:|---:|---|
| 합성 mono | 144000 | 1 | 1 | 144000 | bit 동일 |
| CHZZK 1080p stereo | 479999 | 2 | 1 | 480000 | bit 동일 |
| CHZZK 720p stereo | 480001 | 1 | 2 | 480000 | bit 동일 |

합성 입력은 sample 48000을 0으로 채우고, overlap의 나중 입력 sample 96000을 제거했다. CHZZK는 G2의 실제 decoded frame 시각으로 배치 모델을 만들고 같은 파일의 target PCM을 비교했다. 양 채널 모두 모델과 같았으며 그 밖의 값은 바뀌지 않았다. 이 비교는 **AAC 인코딩 전의 lossless PCM**이다. 인코딩 후 AAC의 파형을 bit 동일하다고 주장하지 않는다.

보정 후 AAC 출력들은 target의 연속 presentation union과 정확한 선언 끝을 유지했다. CHZZK 두 파일의 유효 sample 수는 480000이고 raw decode는 480256으로 coded tail 256이 있었다. 반면 timestamp 유지 파일은 raw sample 수도 480256이지만 마지막 decoded 시각은 1080p 480257·720p 480255였다. raw 길이 차이 하나로 유효 끝·tail·gap을 계산할 수 없다는 추가 근거다.

이 후보는 데이터를 명시적으로 재구성하는 동작이다. **선택 포맷 기준 채택만으로 gap fill·overlap 우선순위까지 승인된 것으로 해석하지 않는다.** 획득 누락을 0으로 채워 숨기는 경로도 아니다. source/local mapping과 필요한 encoded packet coverage가 먼저 확인되어야 한다.

## 5. 후보 판정과 남은 하위 계약

| 방향 | 이번 실증 | 후속 판단 |
|---|---|---|
| source gap/overlap을 정확히 유지한 AAC | 개수·끝만 같고 위치는 바뀜 | 이 경로로 정확한 위치 보존을 보증하지 않음 |
| 확인한 source의 연속 sample-grid 재구성 | 세 sample-aligned 입력에서 값 모델·AAC 유효 끝 일치 | 권고 후보. 데이터 변경 의미·비정렬 boundary 규칙·검증 범위를 명시한 뒤 채택 |
| 불연속 입력의 Accurate 실행 거절 | 별도 제품 구현 없음 | 보정 정책을 채택하지 않을 경우의 보수적 대안 |

다음은 **G3c sample-grid/끝 경계 하위 계약 정리와 비정렬 sample·A/V 표시 끝 대조**다. 현재 대조는 모두 48kHz sample-aligned boundary다. 임의 sample rate, audio sample 사이의 IN/OUT, VFR·video 마지막 frame 표시 길이는 검증하지 않았다. AAC output의 byte 동일성 대신 무엇을 보증하는지와 초기 decoder preparation(G3a)도 함께 통합해야 한다. 새 보정 정책·numeric 허용치·4단계 제품 서비스는 아직 확정하지 않는다.

## 실제 수행·검증·커밋

최초 합성 입력은 filter 쉼표 escaping 오류로 실패했다. 실패 로그를 남기고 escaping을 고친 뒤 이미 만든 두 AAC 기준 파일을 보존해 재개했다. 총 **35개 호출 중 초기 실패 1개·이후/기존 성공 34개**, 정상 호출의 root/descendant 종료·reader drain과 모든 probe의 완전한 stdout을 확인했다. 세 harness 빌드는 최종 경고/오류 0이었다. 정상 도구 종료와 기본-clock/위치 보존 검증 실패는 구별했다.

[계획·명령·실행·packet/frame/edit·PCM 배치 모델 증거](evidence/PHASE_1_G3B_AUDIO_ENDS.json)에 실패와 후보별 실제 결과를 포함한다. media·raw 로그·harness는 ignored `.artifacts`에 남긴다. 제품 코드가 바뀌지 않아 전체 제품 테스트는 반복하지 않았다.

이번 논리적 커밋은 결과 문서·증거 1개·하위 계약안·상세 설계·baseline **5개 파일**이다. staging/commit·Phase 2·WPF 다운로드 UI·ClipEditor·제품 코드 구현은 수행하지 않았다.
