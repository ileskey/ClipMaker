# Phase 1 G3c — 비정렬 sample·A/V 표시 끝과 하위 계약안

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `d8dadb80517976731c6b26306e7b1effcbbdf610`, branch `phase/1`, 작업 트리 clean. G3b 커밋과 기존 선택 포맷 시간 의미를 확인했다. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **G3c 제한된 비정렬 boundary·실제 A/V 출력 대조 완료, 제5절 처리 규칙은 사용자 채택 완료**. 정책 채택과 제품 코드·G4/G5·전체 계약 통합·4단계 준비·Phase 완료를 구별한다. 기존 일반 정책과 선택 포맷 시간 의미를 다시 승인받는 문서가 아니다.

## 사전 범위·검증 기준

알려진 4초 lossless A/V 입력을 로컬에서 만들었다. 영상은 160×90·30000/1001fps FFV1, 음성은 sample 위치를 구별할 수 있는 mono float PCM이다. 48kHz와 44.1kHz를 대조했고 이미 획득한 G2 CHZZK 두 prefix도 사용했다. 도구는 기존 FFmpeg/ffprobe 8.1·SDK 10.0.302다. 새 원격 media·설치·제품 변경은 없다.

사전 기준은 원본 video PTS `[IN,OUT)` 집합의 인코딩 전후 일치, 선택한 PCM source index/값의 독립적인 array slice 대조, audio sample 선택과 AAC timestamp 변환의 구별, video 마지막 PTS와 표시 끝의 구별이다. `-t`를 추가하면 표시 끝이 맞을 것이라고 가정하지 않고 대조했다. 모든 도구 호출은 60초·기존 종료/reader 확인을 적용했다.

48kHz 대조 clock은 480000이었다. 실제 source video time_base는 1/60000, audio는 1/48000이며 half-sample 경계를 표현하도록 clock을 선택했다.

| 사례 | IN ticks | OUT ticks | 길이 | audio IN/OUT 위치 |
|---|---:|---:|---:|---|
| video-aligned | 704704 | 1185184 | 480480/480000초 | 70470.4 / 118518.4 samples |
| neither-aligned | 704706 | 1185188 | 480482/480000초 | 70470.6 / 118518.8 samples |
| half-sample | 704705 | 1185185 | 480480/480000초 | 70470.5 / 118518.5 samples |

각 boundary의 exact 값으로 video를 선택했다. 음성 대조는 source native sample을 선택한 뒤 위상을 유지하는 native-phase, 올림 boundary로 선택해 그 grid를 기준으로 출력하는 ceil-grid, 최근접 짝수 boundary로 선택하는 nearest-grid다. 대조 당시에는 미채택이었으며 최종 적용 정책은 제5절을 따른다. native-phase·nearest-grid·wrong-unit 대조를 채택된 실행 경로로 사용하지 않는다.

## 1. atrim option의 단위는 audio sample-rate 기준

FFmpeg n8.1 `atrim`은 incoming frame PTS를 1/sample_rate로 환산하고 start_pts/end_pts와 비교한다. filter link에 `asettb`를 적용했다고 이 option의 단위가 공통 clock으로 바뀌지 않는다. [n8.1 trim.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavfilter/trim.c)

잘못된 대조군은 common clock 480000의 길이 480480을 `atrim end_pts`에 그대로 넣었다. 기대 48048 samples 대신 **121530 samples**가 남았고 audio 끝은 약 2.531875초였다. video의 기대 30개 PTS는 그대로 맞았다. 단위 오류를 file 존재·video 성공·프로세스 exit 0만으로 놓칠 수 있는 반례다.

audio option은 확인한 native sample 범위로 전달하고, 일반 signed 유리수 시간·공통 clock tick·native sample index를 타입/변환 경계에서 구별해야 한다. G3b의 clock 48000이 audio rate와 같았던 결과를 임의 common clock의 근거로 확대하지 않는다.

## 2. source sample 선택과 AAC의 phase 변환

48kHz 세 사례에서 ceil 방식은 source indices **`[70471,118519)`**, 48048개 값을 선택했고 실제 lossless PCM은 그 source array slice와 bit 동일했다. nearest-even은 video-aligned/half-sample에서 `[70470,118518)`을 선택해 요청 전 sample을 포함하고 요청 안 마지막 sample을 제외했다. neither-aligned에서는 최근접과 올림이 우연히 같았다.

| 방식 | source sample-point membership | 상대 출력 시각의 의미 |
|---|---|---|
| native-phase | ceil 선택, 기대 값 일치 | user IN과의 정확한 fractional phase를 filter에 전달하지만 AAC native time_base에서 변환됨 |
| ceil-grid | ceil 선택, 기대 값 일치 | 계산한 올림 boundary를 audio render 기준으로 사용. fractional 원점을 그대로 유지하는 것은 아님 |
| nearest-grid | 일부 사례 실패 | 최근접 boundary를 render 기준으로 사용. 범위 밖 sample이 들어갈 수 있음 |

native-phase의 최초 filter PTS는 video-aligned에서 6/480000초, half-sample에서 5/480000초였다. 그러나 AAC 출력은 첫 packet PTS -1023·skip_samples 1023, 이어지는 packet PTS 1이었다. decoded 첫 frame은 PTS 0·1 sample이고 다음 frame은 PTS 1·1024 samples였다. audio declared end는 선택 값 48048개보다 1 sample 긴 48049가 됐다.

FFmpeg audio frame queue가 timestamp를 sample-rate time_base로 환산하고 encoder delay를 처리하는 코드와 이 결과가 일관된다. 공통 movie/video clock의 정밀도를 올리는 것만으로 native AAC phase의 모든 값을 그대로 보존하지 못한다. [n8.1 audio_frame_queue.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavcodec/audio_frame_queue.c)

ceil-grid는 표준 -1024/skip 1024와 첫 decoded PTS 0을 유지하고 48048 samples의 유효 끝을 기록했다. source 시각의 fractional 잔여를 제거하는 **명시적인 boundary 양자화**이며 이를 숨기거나 일반 A/V offset 전체를 각 stream STARTPTS로 지우는 것과 혼동하지 않는다. 실제 audio 시작이 늦으면 그 지연을 없애는 규칙을 제안하지 않는다.

## 3. 44.1kHz 대조

두 번째 입력의 source video time_base 1/60000·audio 1/44100에 맞춘 clock은 8820000이다. video의 같은 44~74 frame boundary는 IN 12948936·OUT 21777756 ticks였다.

ceil native bounds `[64745,108889)`의 **44144 samples**는 실제 source slice와 bit 동일했고, 출력 video PTS 30개도 기대와 같았다. 요청 길이는 8828820 ticks, audio 끝은 8828800 ticks였다. 이 -20 ticks(약 -2.268µs)는 입력 범위의 정수 sample 선택으로 계산되는 차이다. 고정 48000 clock·항상 정확한 실수 audio 길이·임의 오차 허용치로 일반화하지 않는다.

## 4. video PTS 선택과 표시 끝은 다른 검사

합성 출력의 video PTS는 모든 후보에서 30개씩 정확히 같았다. video-aligned 사례는 마지막 frame의 끝도 요청에 맞았지만 neither/half-sample 사례는 마지막 frame PTS가 OUT 직전에 있어 그 frame을 끝까지 표시하면 OUT을 넘었다.

neither-aligned의 마지막 video PTS는 480478, packet end는 496494 ticks였다. 요청 길이 480482에 비해 **16012/480000초(약 33.358ms)**가 길었다. 같은 ceil-grid에 output `-t`를 추가해도 frame 집합·packet end는 바뀌지 않았다. `-t`만으로 정확한 표시 종료를 보증하지 않는다.

기존 CHZZK target도 보정 후보를 적용해 실제 H.264/AAC MP4 A/V 출력으로 확인했다.

| 항목, clock 48000 | 1080p | 720p |
|---|---:|---:|
| 기대/실제 video PTS 수 | 600/600 | 600/600 |
| video PTS 집합 | 정확히 일치 | 정확히 일치 |
| 첫 video PTS | 65 | 185 |
| 마지막 video PTS | 479249 | 479385 |
| video packet end | 480065 | 480185 |
| audio 유효 끝 | 480000 | 480000 |
| 요청 10초 이후 표시 범위 | 65/48000초 | 185/48000초 |

출력은 선택한 원본 frame만 포함했지만 movie의 표시 길이는 요청 구간 길이와 같지 않았다. 14개 사례 모두 마지막 선택 source packet의 PTS·duration으로 계산한 표시 끝이 실제 output video packet end와 정확히 같았음을 별도로 대조했다. 이것을 source/local mapping 실패·새 frame 추가·임의 몇-frame 허용치로 설명하지 않는다. **원본 PTS 선택 정확도와 stream/movie 표시 범위를 별도로 기록**해야 한다.

## 5. 하위 처리 계약 — 사용자 채택 완료

실증 결과를 바탕으로 제안한 다음 묶음을 2026-10-11 사용자가 채택했다. 채택 답변은 “확인된 source gap은 0으로 채우고 중복은 앞선 값 유지, audio 경계는 sample 단위 올림, video는 IN/OUT 안의 프레임을 선택하고 실제 표시 끝을 안내”다. 원본 시간 0·일반 처리 순서·기존 실패 gate는 유지한다.

1. **확인된 source sample-grid 재구성**: G2 mapping/coverage가 확인된 source의 빈 sample 시각은 0으로 채우고, 같은 시각의 중복은 앞선 값을 유지한다. 획득 누락·unknown mapping을 이 보정으로 숨기지 않는다. 실제 삽입/제거 수와 제한을 기록한다. G3b의 값 모델 검증 범위를 사용한다.
2. **audio IN/OUT 올림**: 확인한 audio lattice의 sample 시각이 `[S_IN,S_OUT)`에 속하도록 native indices를 둘 다 ceil로 계산한다. 기준 sample 시각 Gₐ, rate R일 때 `[ceil((S_IN−Gₐ)R), ceil((S_OUT−Gₐ)R))`다. phase를 0이라고 가정하지 않는다. 검증하지 못한 lattice/입력은 보증하지 않는다.
3. **audio 출력 원점의 명시적 양자화**: 계산한 IN sample boundary를 audio render 기준으로 사용하고, user exact IN과의 차이·선택 sample 수로 기대한 출력 PTS/끝을 검증한다. 실제 첫 available sample을 STARTPTS로 삼아 지연을 지우지 않는다. 차이를 임의 고정 tolerance로 통과시키지 않는다. native boundary/코덱 표현이 맞는지 검사한다.
4. **video는 채택한 PTS membership 유지**: `[S_IN,S_OUT)` frame 집합과 출력 상대 PTS를 검증한다. 선택한 마지막 frame을 끝까지 표시하는 범위를 실제 출력 끝으로 기록한다. 요청 OUT과 container/movie 끝이 같다는 별도 보증은 하지 않는다. 확인한 source frame duration/표시 범위로 검증하며 이를 알 수 없으면 보증하지 않는다. 예상과 다른 frame·PTS·표시 범위는 실패시키며 실제 길이/제한을 결과에 전달한다.
5. **codec tail은 확인된 metadata로 분리**: stream·packet·edit 근거가 일치하는 presentation 끝으로 검사한다. raw decode sample 수나 duration 문자열 하나로 성공을 정하지 않는다. G3a decoder preparation·값 차이 제한도 유지한다. lossy output의 PCM byte 동일성을 보증하지 않는다.

audio render의 양자화는 새 하위 의미이고 source gap fill/중복 우선순위는 실제 값 재구성이다. 사용자의 위 명시 답변으로 이 묶음을 채택했다. metadata/codec 표현 검사·초기 decode preparation·미확인 mapping/coverage 차단과 검사 실패 시 미확정은 계속 유지한다. 아직 제품 구현이나 모든 입력 지원을 선언하지 않는다.

대안은 요청 OUT과 실제 표시 끝을 엄격하게 같게 요구하는 방향이다. 현재 직접 FFmpeg pipeline은 위 비정렬·CHZZK 사례에서 이를 충족하지 못했다. 별도의 표시 길이 처리 경로를 검증하거나 해당 입력을 거절해야 하며 성공·자동 Fast fallback으로 숨길 수 없다. 임의 영상 최적화나 MP4 binary 수정기를 이번에 구현하지 않았다.

## 남은 작업·실제 검증·커밋 범위

채택한 계약으로 G3 결과·후속 API 계획을 통합하고 **G4 Fast·YouTube 분리 A/V/codec·CFR/FPS 검증**으로 진행한다. G5 서비스/오류 통합과 기존 6단계의 4단계 제품 구현 시작 조건은 그 결과로 확인한다. 이번 대조가 모든 codec/rate/lattice/VFR를 보증하는 것은 아니다. G3 경계/보정 정책은 채택 완료이며 G4/G5·전체 준비·Phase 완료는 아직 Pending이다.

최초 harness 빌드는 다중 var 선언 오류(CS0819)로 실패했고 수정했다. 이후 기본·rate-control 빌드는 최종 경고/오류 0이었다. **60개 도구 호출 모두 exit 0·root/descendant 종료·reader drain**, 모든 probe stdout 완전성을 확인했다. 도구 성공과 wrong-unit·phase 변환·membership·표시 끝의 반례는 구별했다. 실제 video PTS·선택 PCM source index/값·반복 `-t` 대조·metadata를 검사했다. 제품 코드 변경이 없어 전체 제품 테스트는 반복하지 않았다.

[계획·실행·source/출력 packet/frame·선택 sample·표시 끝 증거](evidence/PHASE_1_G3C_AV_BOUNDARIES.json)에 근거를 남긴다. media/harness/raw 로그는 ignored `.artifacts`에 보존한다. 원격 URL·인증값은 증거에 포함하지 않는다.

이번 논리적 커밋 범위는 결과 문서·증거 1개·하위 계약안·상세 설계·baseline **5개 파일**이다. staging/commit·제품 코드·Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다.
