# Phase 1 — CHZZK IN 위치·포맷 확대 대조와 원점 후보 반례

수행일: 2026-10-11 (Asia/Seoul).

상태: **두 추가 사례에서 target packet coverage·후보 시간축 trim 재현, 원점 정책 일반화는 실패**. stream start 최소값을 제품의 원본 시간 0으로 채택하지 않는다. 제품 코드·전체 준비·정밀 처리·Phase 1 완료는 아니다.

## 시작과 사전 범위

- branch `phase/1`, 시작 HEAD `83d12122cd52982824b6517d8b5cb8daf202b9d4`: `docs: Record CHZZK preroll acquisition and source-aligned trim`. 깨끗한 작업 트리와 이전 커밋을 확인했다.
- 사용자 지시 "다음 작업 진행하자"에 따라 해결 경로 일반화의 첫 단위로 다른 IN 위치와 720p를 대조했다. 두 primary 사례로 범위를 제한했으며 후속 비교는 모두 이미 읽은 reference·로컬 파일에서 수행했다.
- 두 포맷의 target은 `[91.25,101.25)`초, acquisition은 `[89.25,103.25)`초였다. Reference probe는 source timestamp 축의 `90%106` 창, 호출당 60초·stdout 32MiB·stderr tail 256KiB를 유지했다. 원격 reference 두 번·14초 구간 요청 두 번만 실행했다.
- 같은 source 15430558의 Completed·duration 2667초·명시 ID를 metadata service로 재확인했다. 설치 도구·retry·config/plugin/cache/remote component 차단과 HLS 입력 `extension_picky 0`만 사용했다. 전체 다운로드·다른 품질/모드 fallback·도구/패키지/runtime 설치·쿠키 import는 하지 않았다.
- 새 ignored 폴더를 사용하여 기존 미디어와 사용자 변경을 보존했다. 제품 코드·솔루션·CLI/WPF·ClipEditor·Phase 2·Git staging/commit/tag/branch는 변경하지 않았다.

## 1. 획득·후보 target 대조

시간 원점은 제품 정책이 아니라 **기존 input seek 동작과 비교할 후보**로 잡았다. 소수 start_time을 반올림해서 쓰지 않고 source의 stream `start_pts`·time_base를 읽어 확인한 최소값을 사용했다. 필요한 source/local 분모의 LCM을 구한 이번 공통 clock은 1/48000이었다.

| 항목 | 1080p | 720p |
|---|---|---|
| format | hls-8384 | hls-3192 |
| source video start | 12000/6000 = 2초 | 12599/6000 ≈ 2.099833초 |
| source audio start / 후보 원점 | 95935/48000 ≈ 1.998646초 | 99007/48000 ≈ 2.062646초 |
| 후보 target IN | 4475935/48000 | 4479007/48000 |
| 후보 target OUT | 4955935/48000 | 4959007/48000 |
| prefix MP4 크기 | 15,620,453 bytes | 5,931,219 bytes |
| acquire / source probe / local probe | 모두 exit 0 | 모두 exit 0 |
| 필요 video / 보존 | 600 / 600 | 600 / 600 |
| 필요 audio / 보존 | 470 / 470 | 470 / 470 |

모든 supervisor 호출에서 root·자식·reader 종료와 complete/untruncated stdout을 확인했다. 원본/로컬 payload hash와 PTS를 일치시켜 대응을 구했으며 PTS를 DTS로 대체하지 않았다.

1080p의 유일 anchor는 video 855·audio 668개, 720p는 video 854·audio 657개였다. 모든 anchor가 각각 하나의 offset을 보였다. 720p의 audio 10개는 reference 창 바깥 또는 변경 여부를 확인하지 못한 항목으로 남겼지만 target의 필요 audio 470개는 모두 대응했다. ambiguous/unresolved 대응은 0이었다. 동일 hash의 첫 항목을 임의 선택하는 규칙을 쓰지 않았다.

## 2. stream별 source/local map과 trim

같은 source target으로 옮긴다는 방법은 유지하되 이전 보정 상수를 재사용하지 않았다.

| 1/48000 clock | 1080p | 720p |
|---|---|---|
| video source−local offset | 4380000 | 4383192 |
| audio source−local offset | 4379935 | 4383007 |
| local target IN, video | 95935 | 95815 |
| local target IN, audio | 96000 | 96000 |

video/audio를 각각 PTS-STARTPTS로 초기화하지 않았다. 확인한 offset으로 같은 **후보** source target origin에 옮긴 뒤 `trim/atrim start_pts=0:end_pts=480000`을 적용했다. 이전 실험과 같은 libx264 veryfast/CRF18·AAC192k, copyts·fps passthrough·encoder/movie/video timebase 48000을 사용했다. 이번 분모의 적합성만 확인했으며 다른 fps/clock에 대한 일반 설정으로 확정하지 않는다.

| output 대조 | 1080p | 720p |
|---|---|---|
| MP4 크기 | 1,360,890 bytes | 957,413 bytes |
| encode / decode | exit 0 / 0 | exit 0 / 0 |
| 실제/예상 video frame 수 | 600 / 600 | 600 / 600 |
| 전체 video PTS 순서·값 | 예상 후보 집합과 정확히 같음 | 예상 후보 집합과 정확히 같음 |
| 첫 video PTS | 65/48000 | 185/48000 |
| 마지막 video PTS | 479249/48000 | 479385/48000 |

이 성공은 **선택한 후보 경계를 정확히 실행했다**는 의미다. 후보 원점 자체가 사용자 원본 시간의 권위 있는 기준이라는 검증은 아니다. 후보로 기대값을 만들고 trim이 그 기대값과 같다는 이유만으로 올바른 원본 IN을 증명하는 순환 검증을 하지 않는다.

## 3. 품질 간 동일 payload 비교 — 원점 후보의 반례

두 source reference에 공통으로 있던 유일 AAC payload 661개를 직접 비교했다. 이는 재인코딩된 output끼리의 비교가 아니라 source encoded audio끼리의 SHA-256/PTS 비교다.

| 720p PTS − 1080p PTS, time_base 1/48000 | packet 수 |
|---|---|
| -1 sample | 180 |
| 0 | 301 |
| +1 sample | 180 |

즉 관찰한 창의 동일 음성 payload들은 두 포맷에서 거의 같은 raw source clock에 있었다. 반면 stream start 최소값의 차이는 **3072 samples = 64ms = AAC 3 frames**다. 같은 숫자 IN에 포맷별 후보를 더하면 동일 내용에 대한 상대 target이 약 64ms 달라진다. 이는 실제 해당 창의 반례이며 모든 시점/포맷의 clock 공유를 자동 보증하지 않는다.

- 후보를 각각 적용한 target: audio 470개씩 중 공통 hash **467개**, 각 포맷만 선택한 hash **3개씩**.
- 원격 요청 없이 target을 같은 raw source `[4475935,4955935)`로 고정한 control: 470개씩 중 공통 hash **470개**.
- control에 1080p의 후보 숫자를 재사용한 것은 차이의 원인을 분리한 실험이다. 1080p가 제품의 정답 원점을 제공한다고 채택하지 않았다.

**판정:** packet coverage와 affine PTS 변환의 재현은 통과했지만, `min(stream.start_pts)`를 source 전체의 원점으로 사용하는 일반화는 채택할 수 없다. 화질만 바꿨는데 같은 IN/OUT의 내용이 달라지는 경로가 된다. video start만 쓰거나 항상 2초를 더하는 새 추정값으로 대체하지 않는다.

[FFmpeg seek 설명](https://ffmpeg.org/ffmpeg.html#Advanced-options)의 `seek_timestamp` 기본 동작은 input start time에 상대적인 seek이고, input stream copy는 앞선 seek 지점의 구간을 보존할 수 있다. 도구의 input-relative 시간과 제품의 품질과 독립적인 원본 시간을 분리해야 한다. 이번 헤더 start 값을 읽을 수 있다는 사실 자체는 후자의 권위 있는 원점 증거가 아니다.

## 4. sample boundary 대조

local prefix를 decode한 audio frame PTS/nb_samples를 source map으로 옮겨 target과 교차하고, output AAC decode를 유효 `[0,480000)` 구간에서 다시 계산했다. 결과가 양쪽 사례마다 일치했다.

| 관찰 | 1080p | 720p |
|---|---|---|
| 선택 source decoded samples 합계 | 479999 | 480001 |
| source gap / overlap sample 수 | 2 / 1 | 1 / 2 |
| output 유효 sample 교차 합계 | 479999 | 480001 |
| output gap / overlap sample 수 | 2 / 1 | 1 / 2 |
| raw output decoded samples | 480256 | 480256 |
| AAC 마지막 decoded frame PTS | 479233 | 479231 |
| AAC 마지막 유효 duration | 767 | 769 |

양쪽 마지막 유효 끝은 480000 ticks지만, frame/sample timestamp의 gap/overlap 때문에 총 sample 수는 다르다. container elapsed duration·raw sample 총합·유효 presentation coverage가 같은 개념이 아니다. 무음 삽입·중복 sample 삭제·CFR 강제로 수를 맞추지 않았다. sample timestamp 정책·encoder priming/끝 padding·player 호환성은 계속 보류한다.

## 판정과 다음 선행 작업

1. **확인:** 이 source의 fractional IN 두 포맷에서 2초 앞뒤 여유가 필요한 target 데이터를 확보했고 같은 변환 방법으로 후보 경계를 실행했다.
2. **반례:** 포맷별 stream minimum은 header 값과 tool input-relative 기준일 수 있으나 제품의 공통 원본 시간 0을 보장하지 않는다. 이전 `[90,100)` 실험의 source target도 관찰한 해당 경로의 기준으로 유지한다.
3. **미확정:** 고정 2초 여유·고정 local IN·stream별 start_pts 최소값·원본 전체 CFR/VFR·일반 sample 경계 정책을 확정하지 않는다. 단순 decoder 성공으로 제품 완료를 선언하지 않는다.

다음 선행 단위는 **품질과 독립적인 source 시간 원점의 근거 확인**이다. 동일 source의 playlist/fragment 시간 관계·가능한 provider 시간 기준과 알려진 nonzero PTS 로컬 기준을 대조해, 사용자 IN/OUT에서 공통 source timestamp로 가는 계약을 정의해야 한다. 이번 common-target control은 상대 일관성만 보여주므로 원본 시간 0의 권위 증거를 추가해야 한다. 다른 포맷의 earliest sample, metadata 소수 FPS, 임의 2초를 그 증거로 대체하지 않는다.

그 후 확정된 target에서 coverage를 다시 확인하고 sample/표시 종료/padding 검증 계약과 4단계 서비스를 구현한다. 이미 채택한 시간 입력·품질/모드 정책을 다시 승인받는 작업은 아니다. YouTube VP9 header/분리 A/V·로컬 기준 실험·전체 Phase 검증도 남아 있다.

## 산출물과 실제 검증 범위

[실행·mapping·선택/sample 대조](evidence/PHASE_1_CHZZK_GENERALIZATION.json), [품질 간 661 payload 대응](evidence/PHASE_1_CHZZK_CROSS_FORMAT_AUDIO.json), [packet 대응과 output PTS/sample](evidence/PHASE_1_CHZZK_GENERALIZATION_PACKET_FRAMES.json)에 안전한 scalar 증거를 보존했다. URL·headers·cookies·signature·원시 metadata·미디어 payload를 넣지 않았다.

프로그램·더 상세한 allowlist packet reference·미디어는 ignored `.artifacts/phase1-chzzk-generalization-20261011-01/`에 보존했다. 다른 IN/화질로 scope를 확대해 반복 요청하지 않았고 control/sample 감사는 네트워크를 쓰지 않았다. 임시 프로그램 최종 빌드는 경고/오류 0이었다. 제품 변경이 없어 이전 제품 테스트 451개는 다시 실행하지 않았다. 관계/coverage·encode/decode·frame/sample 검사는 실제 수행한 결과이며 전체 설계·구현·필수 사이트 검증 완료로 간주하지 않는다.

이번 단위 권장 메시지: `docs: Record CHZZK origin counterexample across formats`.
