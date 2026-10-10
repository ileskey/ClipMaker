# Phase 1 — CHZZK 여유 구간 획득·source 시간축 trim 대조

수행일: 2026-10-11 (Asia/Seoul).

상태: **시작 부분 음성 보존 가설·목표 영상 PTS 집합·선택 PCM 수 대조 통과**. sample timestamp gap/overlap 처리, AAC presentation/padding 검증 정책과 일반화는 미확정이다. 제품 획득/처리 서비스·정밀 출력·전체 Phase 1 완료는 아니다.

## 시작·실험 범위

- branch `phase/1`, 시작 HEAD `a70f29638a44a2ba068b3638af859dafd8888d11`: `docs: Record CHZZK acquisition audio coverage loss`. 이전 진단 커밋과 깨끗한 작업 트리를 확인했다.
- 사용자 지시 "다음 실험 진행하자"에 따라 [이전 계획](PHASE_1_CHZZK_AV_TIMING_DIAGNOSIS.md#다음-단위--여유-구간-획득-대조)의 같은 source `15430558`·hls-8384·[88,102)초 요청을 한 번 실행했다. 목표 작업은 기존 [90,100)초의 시작 부분 음성 보존·로컬 trim 대조다.
- metadata로 Completed·duration 2667초·동일 format 존재를 재확인했다. 기존 config/plugin/cache/remote component 차단, retry, 명시 FFmpeg bin·`ffmpeg_i1:-extension_picky 0`를 유지했다. 전체 다운로드·다른 format/mode fallback·쿠키 import·도구/runtime/패키지 설치는 하지 않았다.
- 새로운 ignored 출력 위치를 사용했다. 기존 미디어·사용자 변경을 덮어쓰지 않았고 Git staging/commit/tag/branch 작업은 하지 않았다. 제품 코드·솔루션·CLI/WPF·ClipEditor·Phase 2는 변경하지 않았다.

## 1. 여유 획득과 coverage

획득 명령의 구간만 `--download-sections *88-102`로 바꿨다. yt-dlp는 FFmpeg에 `-ss 88 -t 14 -extension_picky 0 -i ... -c copy`를 전달했다. 네트워크 요청 한 번, 6.320초, exit 0이었다. root·자식·reader 종료와 양쪽 로그의 untruncated 상태를 확인했다. 최종 MP4는 **14,333,913 bytes**이며 probe/decode도 통과했다.

| 획득 관찰 | video | audio |
|---|---|---|
| codec / format | H.264 1920×1080 | AAC 48kHz stereo |
| local start | 0.001초 | 1.002초 |
| local duration | 14.000초 | 13.013333초 |
| time_base | 1/12000 | 1/48000 |
| packet 수 | 840 | 610 |

여유 파일의 처음에도 약 1초의 음성 시작 차이가 있다. 가설은 이 시작 문제 자체가 사라진다는 뜻이 아니라, **목표 구간이 그 뒤에 놓여 필요한 데이터를 확보한다**는 것이다.

이전 짧은 source reference를 재사용하여 payload SHA-256·PTS를 비교했다. 추가 원격 probe는 하지 않았다. 참조 창과 유일하게 일치한 packet은 video 780·audio 563이며, 모두 stream별로 같은 PTS 오프셋을 보였다. 나머지 video 60·audio 47은 참조 창 바깥 또는 변경 여부를 구분하지 못한 항목으로 보존했고 여유 파일 전체의 source 대응이 검증됐다고 선언하지 않았다.

| 목표 구간의 필요한 데이터 | 필요 수 | 여유 획득에서 hash 일치 |
|---|---|---|
| 이전 획득에서 없던 full AAC packet | 47 | **47** |
| IN 경계를 가로지르는 AAC packet | 1 | **1** |
| IN/OUT과 겹치는 전체 AAC packet | 470 | **470** |
| half-open 목표 source PTS 안의 영상 packet | 600 | **600** |

이번 source/format/구간에서는 여유 획득이 앞부분 음성 coverage를 확보했다. 모든 영상·사이트에서 고정 2초 여유가 충분하다는 정책으로 일반화하지 않았다.

## 2. source↔local 대응과 공통 target

이전 직접 획득에서 대응으로 확인했던 source target은 다음과 같다. 이는 **기존 요청 경로의 관찰된 source 기준**이며, API/player 시간 0의 일반 의미를 새로 확정한 값이 아니다.

- source target: `[4415935,4895935)` ticks, time_base 1/48000. 길이 480000 ticks = 10초.
- 여유 video relation: source PTS×8 = local PTS×4 + **4319952**.
- 여유 audio relation: source PTS = local PTS + **4319967**.
- 오프셋 차이: -15/48000초 = -0.3125ms. MOV의 표시 시간 양자화를 원본 전체의 drift로 단정하지 않는다.

따라서 target IN의 local 위치는 video `95983/48000 ≈ 1.999645833초`, audio `95968/48000 ≈ 1.999333333초`다. 단순히 양쪽 모두 2초를 빼거나 독립적인 PTS-STARTPTS를 적용하지 않았다. stream별 대응을 통해 **같은 source target origin**으로 옮긴 뒤 공통 `[0,480000)` 경계를 적용했다.

## 3. 로컬 trim/encode 대조

실제 filtergraph는 다음과 같다. 숫자는 위 hash/PTS 대응에서 계산한 이번 사례의 값이며 제품 상수가 아니다.

```text
[0:v:0]settb=expr=1/48000,setpts=PTS-95983,
        trim=start_pts=0:end_pts=480000[v];
[0:a:0]asettb=expr=1/48000,asetpts=PTS-95968,
        atrim=start_pts=0:end_pts=480000[a]
```

입력에는 `-copyts`, 출력에는 libx264·veryfast·CRF 18 / AAC 192k를 사용했다. `-fps_mode:v passthrough`, encoder time_base 1:48000, video_track_timescale와 movie_timescale 48000으로 이번 source clock의 정수 PTS를 보존했다. `-r`로 CFR을 강제하거나 hybrid trim/하드웨어 가속을 추가하지 않았다.

[FFmpeg trim/atrim 문서](https://ffmpeg.org/ffmpeg-filters.html#trim)에서 end가 처음 제외할 frame/sample이고 filter가 timestamps를 자동 초기화하지 않는다는 동작을 대조했다. [encoder time_base 옵션](https://ffmpeg.org/ffmpeg.html#Advanced-options)도 확인했다. 이번 source의 분모에 적합했던 48000을 다른 모든 fps/time_base에 적용하지 않는다.

변환 exit 0, decode exit 0, root·자식·reader 종료와 수집 완전성을 확인했다. 결과 MP4는 **1,317,040 bytes**다.

| 출력 관찰 | video | audio |
|---|---|---|
| codec | H.264 | AAC 48kHz stereo |
| time_base | 1/48000 | 1/48000 |
| start | 65/48000 ≈ 0.001354초 | 0 |
| summary duration | 10.000000초 | 9.999979초 |
| decoded frame 수 | 600 | 469 AAC frames |

컨테이너 duration은 10.001354초다. 마지막 영상 프레임의 표시 길이가 경계를 넘는 것과 해당 프레임 PTS가 target 안에 있는 것은 구분한다. 이를 임의 허용 오차로 통과 처리하지 않았다.

영상의 예상 PTS는 원본 reference에서 `source PTS×8 - 4415935`가 `[0,480000)` 안인 집합으로 계산했다. 실제 decoded video PTS **600개가 전부 같은 순서·같은 값**으로 일치했다. 첫 값 65·833·1649, 마지막 값 477665·478433·479249다. 인위적인 첫 영상 PTS=0 정렬을 하지 않았다. 재인코딩 뒤 바이트/화질이 원본과 동일하다고 주장하지 않으며 source 전체 CFR/VFR도 확정하지 않았다.

## 4. decoded sample과 AAC padding의 분리

trim 전 decoded audio frame의 PTS를 source clock으로 옮기고 각 `[PTS,PTS+nb_samples)`와 target을 교차했다. 실제로 다음을 관찰했다.

- target 안에 걸치는 decoded AAC frame 470개.
- 선택 sample 합계 **479999**, target 시간 폭은 **480000 ticks**.
- 1-sample gap 두 번: target 상대 tick 95488, 479488에서 관찰.
- 1-sample overlap 한 번. gap/overlap을 상쇄해서 정상으로 단정하거나 무음 삽입·sample 삭제로 조용히 정규화하지 않았다.

같은 source-map/atrim을 거친 PCM 기준 WAV의 duration_ts도 **479999**였으며 decoder/sample 교차 계산과 일치했다. 획득 시작 부분의 packet 누락과 이 미세 timestamp 불연속은 서로 다른 문제다. PCM 출력은 연속 sample 파일이므로 source timestamp의 gap/overlap 구조를 그대로 표현하지 않는다는 제한도 남긴다.

재인코딩 AAC 결과는 다음과 같다.

- 첫 encoder priming packet PTS -1024, Skip Samples=1024를 확인했다.
- raw decode는 469×1024 = **480256 samples**를 출력했다.
- 마지막 packet PTS 479232, 유효 duration **767**, 유효 끝 tick **479999**.
- raw 마지막 frame의 1024 samples와 유효 duration 767의 차이는 **257 samples**다. 앞부분 음성 누락으로 해석하지 않았다.
- metadata에서 얻은 유효 `[0,479999)` 범위를 적용해 다시 PCM으로 decode하면 **479999 samples**이고 기준 WAV와 수가 일치한다. 손실 AAC 재인코딩의 PCM 값이 bit-exact로 같다는 검사는 하지 않았다.

따라서 raw decoded sample 총합만으로 padding을 오류 처리하거나 container duration만으로 sample 경계를 보증할 수 없다. decoder 유효 presentation 구간·priming·끝 padding과 source timestamp 불연속을 따로 검사해야 한다. 이번 대조는 sample 선택 수와 metadata 기반 유효 길이의 일치이며, 일반적인 sample-exact 동기/정확도 정책의 확정은 아니다.

## 판정·잔여 조건

1. **통과:** 여유 획득에서 이전에 빠진 47개와 IN overlap packet을 모두 보존했다. target의 영상/음성 encoded coverage도 확보했다.
2. **통과:** source-map 후 half-open 영상 PTS 600개를 정확히 선택·보존했고 decode가 성공했다.
3. **일치 확인:** decoded source frame 교차 계산·PCM 기준·AAC의 유효 metadata 구간 sample 수가 479999로 같았다.
4. **보류:** source sample gap/overlap의 처리 정책, 영상 표시 종료와 frame PTS 기준의 구분, AAC player/decoder의 end padding 취급·일반 source/time_base·전체 정확도 검증.

HLS 입력 seek 내부의 정확한 누락 원인은 아직 수정하지 않았다. 여유 획득은 이번 target 데이터를 확보한 방법이며 음성 시작 차이 자체를 없애는 전역 수정이 아니다. 형식·IN 위치·segment/keyframe별 coverage 확인 없이 고정 여유나 고정 timestamp 보정을 넣지 않는다.

다음 단위는 알려진 source origin·프레임/음성 sample이 있는 로컬 기준 실험으로 공통 시간축·경계·padding 검증 계약을 구체화하는 것이 적절하다. YouTube VP9 MP4 header 및 분리 A/V 시간축, 다른 구간/source·CFR/VFR·4단계 제품 구현은 계속 남아 있다.

## 기록·커밋 범위

[실행·mapping·경계 JSON](evidence/PHASE_1_CHZZK_PREROLL_TRIM.json), [참조 창의 packet 대응](evidence/PHASE_1_CHZZK_PREROLL_PACKET_MATCHES.json), [영상 PTS/음성 frame samples](evidence/PHASE_1_CHZZK_PREROLL_FRAME_SAMPLES.json), [안전한 획득 로그 발췌](evidence/PHASE_1_CHZZK_PREROLL_ACQUIRE.stderr.txt)에 scalar 증거를 남겼다. URL·headers·cookies·signature·원시 metadata·미디어 payload는 없다.

실험 프로그램·미디어는 ignored `.artifacts/phase1-chzzk-preroll-20261011-01/` 및 `.artifacts/phase1-chzzk-preroll-analysis-20261011-01/`에 보존했다. 원격 획득은 한 번이고 그 이후 검사는 로컬 파일·기존 reference만 사용했다. 최종 임시 프로그램 빌드 경고/오류 0, 원격/로컬 실행의 정상 종료와 수집 완전성을 확인했다. 제품 코드가 바뀌지 않아 제품 테스트 451개는 재실행하지 않았다.

이 단위의 권장 메시지: `docs: Record CHZZK preroll acquisition and source-aligned trim`.
