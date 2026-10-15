# Phase 1 G1 — 선택 포맷 원점·seek 좌표 대조

실행일: 2026-10-11 (Asia/Seoul). branch `phase/1`, 시작 HEAD `481958d790c44bdd4d28356ced1d3ae6fb59540f`.

사용자는 이전 문서를 커밋했다고 알렸으나 실제 체크아웃에서는 이전 HEAD와 문서 3개의 staged/unstaged 변경을 확인했다. 이를 사용자에게 알리고 보존한 채 독립적인 G1 실험을 진행했다. 기준 commit을 임의로 바꾸거나 index·commit·branch를 조작하지 않았다. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **G1의 제한된 좌표·초기 presentation 대조 완료**. [사용자 채택한 선택 포맷 시간 의미](PHASE_1_SOURCE_TIMELINE_CONTRACT_DRAFT.md)에 대해 실제 측정 근거와 seek 조합의 반례를 확보했다. 범용 원점 측정 구현·G2 coverage·G3 audio/끝 경계·G4 다른 source·전체 4단계 준비/Phase 완료는 별도다.

## 범위·사전 기준

기존 설치 FFmpeg/ffprobe 8.1과 .NET SDK 10.0.302를 사용했다. 새 ignored harness는 `.artifacts/phase1-g1-origin-seek-20261011-01`에만 만들었다. 영상은 160×90·20fps, 음성은 mono 48kHz chirp, 각각 8초로 생성했다. source를 전체 decode한 frame hash/PCM reference와 부분 seek 결과를 비교했다. 각 영상 비교는 첫 20 frame, 음성은 최대 48 decoded chunk다. 동일 길이의 음성 구간이라고 가정하지 않고 실제 출력 sample 수만큼 reference slice를 비교했다.

| 입력 | 코덱·컨테이너 | 알려진 video 시작 | 알려진 audio 시작 | presentation 원점 O |
|---|---|---:|---:|---:|
| audio-first | FFV1·PCM/NUT | 5.25초 | 5초 | 5초 |
| video-first | FFV1·PCM/NUT | 5초 | 5.25초 | 5초 |
| aac-edit | H.264(B-frame)·AAC/MP4 | 0.25초 | 0초 | 0초 |

사용자 IN은 정확히 1.525초다. 두 NUT의 source target은 6.525초, MP4는 1.525초다. 알려진 생성 기준을 먼저 정하고 source stream 시작·첫 decoded presentation을 독립적으로 비교했다. seek 입력은 알려진 O로 계산했다. 아직 제품의 일반 origin finder를 구현한 것은 아니다.

사전 기준은 초기 presentation과 알려진 시작의 일치, 상대/절대 좌표의 frame/audio 대응, `copyts` 유무의 설명 가능한 PTS 이동, priming과 사용자 원점 구별이다. 모든 외부 명령은 60초·기존 ProcessRunner 종료/reader 검사를 적용했다. 새 원격 미디어 요청·전체 VOD 다운로드·도구/패키지 설치는 수행하지 않았다. 비교용 전체 decode는 이미 생성한 8초 로컬 입력에만 수행했다.

## 1. 원점·음수 packet·edit list

세 입력 모두 stream 시작과 첫 decoded presentation이 알려진 시작과 일치했다. 두 NUT의 video/audio time_base는 `1/48000`, 시작 PTS는 각각 `252000/240000` 또는 `240000/252000`이었다.

MP4 audio는 stream start_pts와 첫 decoded PTS가 0이지만 최초 packet PTS/DTS는 **-1024**, skip_samples는 **1024**였다. audio edit list mediaTime도 1024다. video에는 12000 movie ticks(0.25초)의 empty edit와 mediaTime 4800의 content edit가 있었다. 실제 첫 video presentation은 12000/48000이고 B-frame 때문에 decode 시각과 구별된다.

따라서 음수 AAC priming packet을 포함한 **raw packet 최소값을 시간 0으로 쓰는 알고리즘은 배제**한다. 이번 입력에서 presentation 기준 stream 시작을 첫 decoded frame·skip/edit 정보와 확인하는 방법은 알려진 O에 맞았다. 다른 container·분리 파일·불연속에 동일 규칙을 무검증 적용하지 않는다. 초기 stream start 확인과 중간 probe의 첫 packet을 구별한다.

format.duration도 그대로 사용자 상대 길이라고 일반화하지 않는다. NUT format의 start_time은 5이고 duration 값은 각각 13.200000/13.228667이었다. 생성한 payload 길이와 숫자의 좌표·끝 정의가 다르므로 실제 presentation/coverage와 연결해야 한다.

## 2. seek 경로 대조와 반례

두 NUT에서 기대한 첫 video frame은 source PTS `314400/48000 = 6.55초`다. target 6.525보다 0.025초 뒤의 첫 frame이며 `[IN, OUT)` PTS 선택과 일치한다.

| 경로 | 첫 source video | 20 frame hash | PCM reference slice | 판정 |
|---|---:|---|---|---|
| 기본 상대 `-ss 1.525`, copyts | 6.55초 | 모두 일치 | 모두 일치 | 좌표 대조 통과 |
| 절대 `-seek_timestamp 1 -ss 6.525`, copyts·기본 accurate_seek | 11.55초 | target과 불일치 | 불일치 | **채택하지 않음** |
| 절대 `-ss 1.525`, copyts — 원래 대조군 이름 wrong-absolute | 6.55초 | 모두 일치 | 모두 일치 | 의도한 반대 결과가 나오지 않아 반례로 기록 |
| 기본 상대 seek, copyts 없음 | 6.55초 | 모두 일치 | 모두 일치 | 출력 first PTS=0.025초 |
| 변환한 절대 seek, copyts 없음 | 6.55초 | 모두 일치 | 모두 일치 | 출력 first PTS=0.025초 |
| 절대 여유 seek·noaccurate_seek + 명시 source PTS trim | 6.55초 | 모두 일치 | 모두 일치 | 좌표 대조 통과 |
| 전체 로컬 decode + 명시 source PTS trim | 6.55초 | 모두 일치 | 모두 일치 | reference control |

여유 seek는 이번 대조에서 target보다 0.5초 앞을 읽고 `trim/atrim`에 정확한 target PTS를 주었다. 0.5초를 제품 preroll 상수로 채택하지 않는다. MP4는 O=0이라 상대/절대 copyts 차이가 드러나지 않았고 모든 경로가 source video 1.55초의 기대한 20 frame을 선택했다.

공식 문서는 기본 input `-ss`에 파일 시작이 더해지고 seek_timestamp가 이를 해제한다고 설명한다. 설치 버전과 같은 FFmpeg n8.1 코드에서 demux seek는 해당 옵션을 고려하지만, copyts의 자동 accurate-seek trim 기준에는 `-ss` 값과 입력 시작을 더하는 경로가 있다. 이번 두 비영점 입력의 **5초 추가 trim**과 맞는다. “절대 시각으로 변환했으니 copyts와 함께 안전하다”는 가정은 사용할 수 없다. [FFmpeg seek/copyts 문서](https://ffmpeg.org/ffmpeg.html#Main-options), [n8.1 ffmpeg_demux.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/fftools/ffmpeg_demux.c)

기본 상대 seek에 사용되는 demuxer 시작을 F라고 하면, 원하는 source target `S=O+U`에 대해 상대 seek 인자 후보는 **`Q=O+U−F`**다. 이번 두 NUT는 F=O=5라 Q=U였다. source presentation 원점 O와 도구 시작 F를 항상 동일하다고 두지 않는다. raw PTS를 보존하려면 검증한 상대 copyts 경로 또는 자동 trim을 분리한 명시 PTS trim 경로를 후보로 삼고, 실제 원격/획득 경로는 G2에서 다시 검증한다.

## 3. AAC 값 차이와 좌표 판정의 분리

MP4 직접 seek의 첫 decoded audio PTS는 `73200/48000 = 1.525초`, normalized 출력은 0이었다. 여유 seek+명시 trim도 첫 decoded PTS 73200이었다. 따라서 이번 target 시간 경계는 확인했지만 음성 값 동일성까지 통과했다고 기록하지 않는다.

| AAC 경로 | PCM sample 수 | 전체-decode slice와 다른 sample | 최대 signed-16-bit 값 차이 |
|---|---:|---:|---:|
| 기본 seek(상대/절대·copyts 유무 모두) | 48656 | 1773 | 1 LSB |
| 0.5초 여유 decode + 명시 trim | 48656 | 1708 | 1 LSB |
| 전체 로컬 decode + 명시 trim | 48656 | 0 | 0 |

차이는 뒤쪽까지 남았다. 짧은 preroll이 모든 AAC decode 값 차이를 해결했다고 주장하지 않는다. 정확한 decoder 경로 차이의 원인·음성 값 허용 정책은 이번에 확정하지 않았다. 최대 1 LSB라는 관찰을 제품 허용치로 채택하지 않으며 G3에서 presentation sample/priming/padding/끝 경계와 함께 분리해서 다룬다. video hash 일치·첫 audio PTS 일치·PCM byte 불일치는 서로 다른 판정이다.

## 4. 기존 CHZZK 초기 fragment와 연결

[기존 초기 segment 0](PHASE_1_CHZZK_SOURCE_ORIGIN_EXPERIMENT.md)의 init+fragment 로컬 파일만 추가 probe했다. 원격 HLS 전체를 재조회한 것은 아니다. 첫 decoded audio PTS가 기존 stream/packet 시작과 같음을 확인했다.

| 선택 format | 확인한 presentation 후보 O | ffprobe format 시작 F | F−O |
|---|---|---|---|
| hls-8384 | 95935/48000초 | 1998646/1000000초 | 1/6000000초 |
| hls-3192 | 99007/48000초 | 2062646/1000000초 | 1/6000000초 |

선택 포맷 기준 채택에 따라 이 두 O가 다르다는 사실 자체는 품질 독립 보증 실패와 구별된다. 기존 generalization의 각 후보 target/map/trim 결과를 선택 포맷 기준의 제한된 증거로 활용할 수 있다. source/current metadata 갱신 뒤에도 같은 파일/시각이라고 무검증 재사용하지 않는다.

tool의 microsecond 시작과 stream의 유리수 O는 약 0.167µs 차이가 났다. 두 값을 하나로 반올림해 저장하거나 이 차이를 자동 허용 오차로 선언하지 않는다. 실제 seek 인자 정밀도·frame/sample 경계에 미치는 영향과 원격 input 시작의 확인은 G2/G3 대상이다. 이 로컬 초기 fragment의 format.duration은 전체 영상 길이가 아니다.

## 실제 검증·다음 단위·커밋 범위

최종 harness 빌드는 경고/오류 0이었다. 보완 과정에서 루프의 변수 범위를 잘못 수정해 CS0103 빌드 실패 1회를 겪었고 수정 후 빌드했다. 영상 생성·probe·decode·부분 seek·추가 PTS 관찰 **68개 호출 모두 exit 0, root/descendant 종료와 stream drain을 확인**했다. 모든 probe stdout은 complete/untruncated였다. 명령 성공과 기대 내용 검증 실패를 구별해 위 반례·AAC 불일치를 유지했다.

[scalar 실행·frame hash·sample 비교·edit/PTS 증거](evidence/PHASE_1_G1_ORIGIN_SEEK.json)에 사전 기준·알려진 입력·실제 결과와 사용한 명령을 기록한다. 파일 인자는 실험 파일명으로 축약했고 원시 미디어·전체 로그·인증값을 커밋하지 않는다. 제품 코드·프로젝트·패키지를 변경하지 않아 전체 제품 테스트는 반복하지 않았다.

다음은 **G2: 선택 포맷 원점과 도구 좌표를 실제 구간 획득에 연결하고 stream mapping·target coverage를 확인**하는 단위다. 기존 입력/로컬 파일 증거를 재사용하고, 필요한 원격 확인은 제한된 구간으로 수행한다. 음성 값·끝 경계 보증은 G3로 남긴다. 기존 채택 6단계·4단계 내 논리적 커밋 순서는 유지한다. G1 한 단위의 완료를 전체 준비·제품 구현 완료로 표시하지 않는다.

이번 변경은 G1 결과·증거, 하위 계약안의 G1 상태, 상세 설계·baseline이다. 앞선 사용자 staged/unstaged 문서를 보존한 채 working tree에 추가했다. staging/commit·Phase 2·WPF UI·ClipEditor 작업은 수행하지 않았다.
