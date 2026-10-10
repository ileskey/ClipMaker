# Phase 1 — CHZZK 1080p 음성 시작 차이 추적

실험·분석: 2026-10-10~11 (Asia/Seoul). 기록일: 2026-10-11.

상태: **획득 파일 앞부분의 음성 패킷 누락과 edit list의 역할 확인, 수정 실험 전**. 제품 코드·전역 시간 보정·4단계 서비스는 구현하지 않았다. 정확한 구간·전체 source 시간축·CFR·Phase 1 완료는 Pending이다.

후속 [여유 획득·trim 대조](PHASE_1_CHZZK_PREROLL_TRIM_EXPERIMENT.md): [88,102)초에서 빠진 47개와 IN overlap packet을 모두 확보했다. 같은 source target으로 옮겨 영상 PTS 600개·선택 PCM 수·AAC 유효 sample 수를 대조했다. sample timestamp 불연속·padding·일반화 검증은 남아 있으며 위 수정 전 상태와 아래 계획은 최초 진단 당시 기록이다.

## 시작·범위

- branch `phase/1`, 시작 HEAD `386d46efd21d211a2c1ea04c9de3e8946d2759e9`: `docs: Record CHZZK HLS diagnosis and option comparison`. 이전 진단/대조 커밋과 깨끗한 작업 트리를 확인했다.
- 사용자 지시 "다음 단계 시작하자"에 따라 [대조 결과](PHASE_1_CHZZK_HLS_COMPARISON.md)의 1080p video start 0.001초 / audio start 1.008초 차이를 추적했다. 먼저 기존 720p/1080p 파일을 재검사했고, 이후 같은 source/format의 짧은 원격 packet 창 한 번만 읽었다.
- 전체 다운로드·다른 화질/모드 fallback·도구/패키지/runtime 설치·cookies import·원시 metadata/서명 URL 저장·Git staging/commit/tag/branch 변경은 하지 않았다. 제품 코드·솔루션·CLI/WPF·ClipEditor·Phase 2는 바꾸지 않았다.

## 1. 로컬 MP4의 지연 표현

FFprobe 기본 읽기와 `-ignore_editlist 1` 읽기를 비교하고, MP4 moov/trak의 mvhd/mdhd/elst를 최소 binary reader로 확인했다. 생성 파일은 수정하지 않았다. movie timescale은 1000, video media timescale 12000, audio media timescale 48000이다.

| 1080p track | empty edit (movie ticks) | 이어지는 content edit | 기본 첫 packet PTS | edit list 미적용 첫 PTS |
|---|---|---|---|---|
| video | duration 1, media_time=-1 → 1ms | duration 10000, media_time=0 | 12/12000 = 0.001초 | 0 |
| audio | duration 1008, media_time=-1 → 1008ms | duration 9003, media_time=0 | 48384/48000 = 1.008초 | 0 |

독립적인 FFprobe trace도 empty edit를 video 12 media ticks / audio 48384 media ticks로 처리했다. [trace 발췌](evidence/PHASE_1_CHZZK_AV_EDIT_TRACE.txt)에 보존했다. [FFmpeg n8.1 MOV muxer](https://github.com/FFmpeg/FFmpeg/blob/n8.1/libavformat/movenc.c#L3808)는 첫 sample의 DTS/CTS로 presentation delay를 계산하여 양수이면 empty edit를 기록한다. [MOV reader](https://github.com/FFmpeg/FFmpeg/blob/n8.1/libavformat/mov.c#L5873)는 edit list를 적용하며 ignore_editlist에서 이를 생략한다. 이번 숫자는 실제 파일/FFprobe와 대조한 것이며 코드 설명만으로 추정한 값이 아니다.

따라서 약 1초의 **표시 시작 차이를 표현하는 구조**는 edit list에서 확인된다. 그렇다고 이 구조가 원본 음성을 잘못 지연시켰다고 확정할 수는 없다. 원래 payload의 시각을 비교해야 한다.

720p의 edit list는 empty edit 대신 video media_time=23754/12000, audio media_time=46848/48000로 앞부분을 잘라 표시한다. edit list를 무시하면 video packet 수가 659→719로 달라지고 preroll이 드러난다. 이 옵션은 조회 실험이며 제품의 일반 수정으로 사용할 수 없다. normal packet 수와 decoded frame 수는 다르며 discard/preroll/B-frame을 혼동하지 않았다.

## 2. 원본의 짧은 packet 창과 payload 대조

- 같은 `15430558`·hls-8384·Completed·duration 2667초를 metadata service로 재확인했다. format의 URL/headers는 Infrastructure 실험 메모리에서만 사용했다.
- FFprobe에 `-extension_picky 0 -read_intervals 88%103 -show_packets -show_data_hash sha256`를 전달했다. 88~103은 **source timestamp 축의 요청**이며 사용자 화면 시간과 같다고 가정하지 않았다. 호출 전체 60초, stdout full 32MiB 상한, stderr tail 256KiB로 실행했다. 명령은 한 번 실행했고 root·자식·reader 종료·exit 0·complete/untruncated stdout 470,152 bytes를 확인했다.
- 원격 media 일부를 읽었지만 영상 파일·원시 metadata·서명 URL·headers는 저장하지 않았다. packet PTS/DTS·time_base·flags·size·SHA-256만 allowlist로 남겼다. 전체 영상이나 전체 프레임을 읽는 검사로 바꾸지 않았다.
- source video time_base=1/6000, audio=1/48000. 실제 관찰 packet 수는 video 900, audio 656이다. 첫 video packet은 PTS 88초, 첫 audio packet은 약 89.017313초다. 창의 앞부분에서도 비대칭 시작을 관찰했으나 HLS seek 내부의 정확한 버그 위치까지 확정하지 않았다.
- 기존 1080p 파일의 모든 compressed packet을 pos/size로 읽어 SHA-256을 계산했다. source packet hash와 일치하는 대상은 각각 하나여야 대응으로 인정했다. 음성/영상 바이트나 이미지를 증거 파일에 복사하지 않았다.

## 3. 전체 대응 결과와 빠진 앞부분

| 항목 | video | audio |
|---|---|---|
| 로컬 packet 수 | 600 | 422 |
| 원본과 유일하게 hash 일치 | 600 | 422 |
| 불일치 / 중복 대응 | 0 / 0 | 0 / 0 |
| 원본−로컬 PTS 오프셋 | 1103988/12000 = 91.999초 | 4415935/48000 = 91.998645833…초 |
| 대응 오프셋 분포 | 같은 값 × 600 | 같은 값 × 422 |

video는 source PTS를 두 배하여 동일한 1/12000 축에서 비교했다. audio는 양쪽 모두 1/48000이다. 두 오프셋 차이는 **17/48000초 ≈ 0.354167ms**다. movie edit가 1ms 단위로 표현되는 것과 양립하는 차이지만, 이를 일반 허용 오차로 새로 채택하지 않았다. 이번 창의 packet 기준으로는 남은 전체 음성이 약 1초 통째로 밀린다는 증거가 없다. 콘텐츠 수준의 모든 source 동기를 검증한 것은 아니다.

첫 원본 video packet은 PTS `552000/6000 = 92초`, 첫 획득 audio packet에 대응하는 원본은 `4464319/48000 ≈ 93.006646초`다. 그 audio가 로컬 `48384/48000 = 1.008초`에 놓인 것은 원래 시간 대응을 유지한다. 이 음성의 edit list를 제거해 0으로 옮기면 원본 video/audio 대응을 바꾸게 된다.

audio 대응으로 구한 이번 획득의 source origin은 `4415935/48000 ≈ 91.998646초`다. 이 origin 이후부터 첫 획득 audio source PTS 이전까지 **원본 AAC packet 47개**가 존재하고, 그 payload hash는 로컬 파일의 모든 audio packet에서 **하나도 발견되지 않았다**. 추가로 origin을 가로지르는 AAC packet 한 개도 원본 창에서 관찰했다. 47개의 full packet 길이는 `47×1024/48000 ≈ 1.002667초`이며, 첫 획득 packet의 시작 차이 1.008초와 같은 숫자는 아니다. packet 경계/부분 sample을 구분한다.

**판정: 이번 획득 파일은 요청 시작 근처의 원본 음성 데이터를 포함하지 않는다. edit list는 남아 있는 늦은 첫 음성 packet의 시각을 표현한다.** edit list/요약 표시만의 문제나 전체 음성의 일괄 시간 이동으로 해결할 수 없다. 이미 없는 packet은 기존 파일을 remux/재인코딩한다고 복원되지 않는다. 원본 packet이 음성인지 무음인지의 청각적 의미는 평가하지 않았으며 encoded audio coverage를 비교했다.

누락 발생 범위는 현재 `HLS 입력 seek/stream copy 획득 경로`까지 좁혔다. 그 내부에서 정확히 demux seek·packet 선택·mux 입력 처리 중 어느 조건이 버리는지와 HLS segment 경계 원인은 추가 대조가 필요하다. MP4 muxer가 임의로 1초를 만든 것이라고 단정하지 않는다. 이번 source/format/window 대응을 다른 사이트나 전체 영상에 일반화하지 않는다.

1080p의 source→local video PTS 대응이 전 packet에서 일정하므로, 이전 16/17ms 간격 혼합은 이 창에서 source PTS와 함께 보존됐다. acquired 출력이 새로 만든 간격 변화로 분류하지 않는다. 원본 전체 CFR/VFR 및 타임코드 계산 기준은 별도 검증으로 남긴다.

## 다음 단위 — 여유 구간 획득 대조

다음에는 같은 hls-8384에서 **[88,102)초의 14초 요청**으로 앞뒤 여유를 둔 획득을 대조한다. 기존 [90,100)초 요청보다 앞선 segment를 포함했을 때 빠진 원본 audio packet이 보존되는지 확인하는 가설이며 아직 실행하지 않았다.

1. source/format과 현재 필요 옵션을 재확인하고 새 output 위치에서 한 번 실행한다. 전체 다운로드·품질/모드 fallback은 하지 않는다.
2. 현재 source reference의 hash/PTS를 사용해 요청 [90,100)초에 필요한 audio coverage를 검사한다. 영상 전체나 여유 구간 전체의 global 시간축 검증으로 해석하지 않는다.
3. source↔local 대응과 **공통 origin**을 먼저 확인한 뒤 로컬 처리 범위를 계산한다. 숫자 2초나 고정 1초 보정을 검증 없이 제품 상수로 넣지 않는다.
4. 필요한 packet이 보존됐다면 로컬 trim/encode를 별도 대조한다. video/audio 각각 PTS-STARTPTS로 독립 초기화하거나 edit list를 제거해 시작 표시만 같게 만드는 경로는 채택하지 않는다.
5. 성공 여부는 stream 존재·decode뿐 아니라 원본 packet/frame 대응, 시작 부분 음성 보존, half-open 영상 프레임 범위·음성 sample 경계·종료를 검사한다. 여유 획득의 성공만으로 Accurate 구현 완료를 선언하지 않는다.

## 산출물·검증·커밋 지점

[진단 JSON](evidence/PHASE_1_CHZZK_AV_TIMING_DIAGNOSIS.json)은 local atoms·normal/ignore-editlist 조회와 packet 요약·source probe 실행/요약·초기 hash 대응·전체 coverage를 포함한다. [전체 1022 packet 대응](evidence/PHASE_1_CHZZK_AV_PACKET_MATCHES.json)은 stream index·source/local PTS·size·hash만 보존한다. 원시 URL/headers/cookies/서명 metadata·미디어 payload는 없다.

ignored `.artifacts/phase1-chzzk-av-timing-20261010-01/`에 임시 프로그램과 더 상세한 안전한 packet 자료를 보존했다. 기존 미디어 파일은 읽기만 했다. 바이너리 atom reader와 FFprobe trace를 독립 대조했으며 모든 supervisor 호출의 정상 종료·수집 완전성을 확인했다. 분석 재실행은 저장 자료만 사용했고 원격 probe를 반복하지 않았다.

임시 프로그램의 source 모드 추가 빌드 한 번은 top-level 함수보다 type 선언을 앞에 두어 실패했다. 선언 순서를 수정한 이후 최종 빌드는 경고/오류 0으로 통과했다. 제품 코드가 바뀌지 않아 이미 통과한 제품 테스트 451개는 재실행하지 않았다. 준비·구현·사이트 검증 완료를 새로 선언하지 않는다.

이번 단위의 권장 메시지: `docs: Record CHZZK acquisition audio coverage loss`.
