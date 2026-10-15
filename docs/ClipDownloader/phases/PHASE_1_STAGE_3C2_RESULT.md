# Phase 1 / 3c 두 번째 논리적 단위 — 실험과 자동 포맷 선택

수행일: 2026-10-10 (Asia/Seoul).

상태: **3c-2 선택 구현·자동 검증 완료, 커밋 대기**. 계획한 다섯 구간 실험은 실행했으나 CHZZK 획득 2건·YouTube VP9 MP4 결과 검사 1건이 실패했다. 실제 사이트/시간축/출력의 전체 필수 검증과 Phase 1 완료는 Pending이다. 자동 선택 성공은 획득·모드 호환성·CFR·정확한 클립의 성공 증거가 아니다.

## 시작·허용 범위

- branch `phase/1`, 시작 HEAD `fca5a64c6f697bef79b8ba7af9c5a2ab23735106`: `feat: Add quality and format selection contracts`. 3c-1 커밋과 깨끗한 작업 트리를 확인했다.
- 사용자가 "다운로드 허용할게. 계속 진행해"라고 지시하여 [기존 계획](PHASE_1_STAGE_3C_EXPERIMENT_PLAN.md)의 최대 다섯 [90,100)초 구간 요청을 실행했다. 이미 채택한 일반 정책을 다시 승인받지 않았다.
- 전체 영상 다운로드·다른 포맷 fallback·실패한 원격 구간 재시도·cookies import·도구/runtime/패키지/remote component 설치는 하지 않았다. yt-dlp 내부의 채택한 최대 2회 추가 retry는 유지했다.
- 제품 획득/처리 서비스·CLI·WPF·Phase 2·ClipEditor·Git staging/commit/tag/branch 변경은 수행하지 않았다. 실험은 package-free 임시 프로그램에서 기존 ProcessRunner를 사용했다.

## 실제 실험

환경: Windows x64, SDK 10.0.302/runtime 10.0.10, yt-dlp 2026.08.19, FFmpeg/ffprobe 8.1 full build, Node 22.14.0. 실행 전 info/clip 범위 tool 진단 및 libx264/aac/mp4 기능을 확인했고, 두 source의 완료 상태·길이·명시 ID 존재·codec/FPS를 재조회했다.

원격 구간은 yt-dlp `--download-sections *90-100`과 표의 명시 ID만 사용했다. 기타 옵션은 기존 계획·3b의 config/plugin/cache/remote component 차단·Node 설정을 유지했다. 내려받은 획득 결과를 다시 로컬 FFmpeg로 copy와 libx264/AAC 변환했다. 원격 요청이 정확히 10초 분량의 네트워크 데이터만 읽는다고 보증하지 않는다.

| 입력 | ID 조합 | 획득 결과 | 로컬 MP4 copy / H.264+AAC 변환 |
|---|---|---|---|
| CHZZK 15430558 | hls-3192 | exit 1, 3.901초, 최종 파일 없음 | 수행하지 않음 |
| CHZZK 15430558 | hls-8384 | exit 1, 3.977초, 최종 파일 없음 | 수행하지 않음 |
| YouTube sFXPM_sfJNo | 137+140 | exit 0, 137.729초, MP4 3,981,543 bytes | copy·변환·probe·decode 모두 exit 0 |
| YouTube sFXPM_sfJNo | 248+251 | exit 0, 138.682초, WebM 2,256,400 bytes | copy exit 0이나 probe exit 1·decode -1094995529. 변환·probe·decode exit 0, A/V offset 미해결 |
| YouTube sFXPM_sfJNo | 399+251 | exit 0, 133.240초, WebM 1,930,571 bytes | copy·변환·probe·decode exit 0, A/V offset 미해결 |

모든 원격 명령에서 root·Job 자식·reader 종료를 확인했다. stderr는 bounded 메모리 수집 후 제한된 오류 flags만 보존했다. CHZZK 두 실패의 상세 원인은 이 기록으로 확정할 수 없다. HTTP 403/일반 download 오류 문자열이 없었다는 관찰만으로 source 상태나 TLS/HLS 원인을 추정하지 않는다. 이후 실패 진단에는 민감값을 제외한 오류 분류를 보완할 필요가 있다.

VP9 copy 결과는 네트워크 호출 없이 해당 로컬 파일을 ffprobe로 재검사했고 `Empty VP Codec Configuration box`·`error reading header`를 확인했다. 파일 생성/exit 0만으로 성공 처리하면 안 된다는 실제 증거다. 이를 모든 VP9 codec의 MP4 불가로 일반화하지 않는다.

### 시간·스트림 관찰

- H.264 획득 영상 duration 10.133008초·음성 10.009002초, 두 stream start 0이었다. copy/변환 후 영상 10.033333초다. 요청 [90,100)초가 정확한 원본 프레임 구간으로 보존됐다고 입증하지 않았다.
- VP9/AV1 WebM summary는 duration 20초·stream start 0이었지만 첫 영상 PTS는 **9979 / time_base 1/1000**, 즉 9.979초였다. summary 시작만 보면 A/V 시작 차이를 놓친다.
- VP9 변환: video start 9.966016초·duration 10초, audio start 0·duration 19.993초. AV1 copy: video start 9.979초·duration 10초, audio start 0·duration 19.993초. AV1 변환에서도 video start 9.966016초가 남았다. decode 성공은 동기 성공이 아니다. 원본에 대한 절대 위치와 보정은 4단계에서 검증해야 한다.
- 각 입력에서 첫 120개 영상 PTS만 검사했다. H.264는 512 ticks × 1/15360 간격 119개, VP9/AV1은 33ms 79개·34ms 40개다. 후자는 1ms time_base의 30fps 양자화와 일치하는 관찰이며, 33/34 간격만으로 VFR을 선언하지 않는다. 어느 경우도 원본 전체 CFR을 보증하지 않는다.
- 단순 copy 인자: `-map 0:v:0 -map 0:a:0? -c copy -f mp4`. 변환 인자: `-c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 192k -f mp4`. explicit trim/PTS 보정을 넣지 않았으므로 제품 Accurate 구현·정확도 통과로 해석하지 않는다.

### 로컬 대조 실험

네트워크 실패와 설치 codec 기능을 구분하려고 640×360 30fps testsrc2·48kHz sine의 2초 영상 세 개를 각각 libx264/AAC·libvpx-vp9/Opus·libsvtav1/Opus로 생성했다. 세 조합 모두 MP4 copy와 H.264/AAC 변환, stream probe·decode에 성공했다. AV1 생성 stderr에는 encoder 진단 출력이 있었으며 stderr가 비어 있었다고 주장하지 않는다. 이는 현재 설치 도구의 짧은 합성 입력 검증이며 실제 source 시간축·플레이어 전체 호환성의 증거가 아니다.

원본 민감 JSON·서명 URL·cookies는 파일에 저장하지 않았다. 안전한 stage/exit/종료 증거·실제 codec/time_base/PTS 관찰은 [JSON evidence](evidence/PHASE_1_3C_EXPERIMENT.json)에 보존했다. 실험 프로그램·미디어는 ignored `.artifacts/phase1-3c-20261010-01/`에 남겨 후속 로컬 재검사에 사용할 수 있다. Git 커밋 대상에 넣지 않는다. 추가 네트워크 획득 없이 검사할 수 있는 자료를 삭제하지 않았다.

## 선택 정책과 trade-off

정책은 metadata의 선언값에 대한 deterministic 선택이다. 시각/청각 품질을 측정한 순위가 아니다. [고정 버전 yt-dlp 정렬 문서](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/README.md#sorting-formats)는 여러 정렬 필드·사용자 지정 순서·ID 동률 처리를 제공하지만, 제품에서 default 배열 순서를 그대로 채택하지 않았다.

| video 비교 순서 | 적용 |
|---|---|
| 1 | 확인된 높이 큰 값. 지정 해상도는 정확히 같은 높이의 후보만 |
| 2 | 정확한 유리수 FPS 큰 값; 같은 높이의 미확인 FPS보다 확인된 값 우선 |
| 3 | 가로 해상도 큰 값 |
| 4 | 동률 codec 우선순위 H.264 > VP9 > AV1 > 기타 확인 문자열 > 미확인 |
| 5 | 동률 transport HTTPS > HLS > 기타/미확인 |
| 6 | 같은 확인 codec family의 video bitrate 추정값 큰 값. 확인값 우선 |
| 7 | 앞선 기준 동률이면 embedded audio Present > Absent > Unknown |
| 8 | 끝까지 동률이면 format ID ordinal 오름차순, StableIdTieBreak 주의사항 |

별도 audio는 video의 embedded audio가 Absent일 때, VideoStream=Absent/AudioStream=Present인 후보만 선택한다. AAC > Opus > 기타 확인 문자열 > 미확인, transport, 같은 확인 family의 추정 audio bitrate, ID 순이다. 통합 A/V를 별도 audio로 대체하지 않으며 미확인 embedded audio에 추가 audio를 붙이지 않는다. 별도 audio 없음은 원본 무음의 증거가 아니고 추가 확인 주의사항을 남긴다.

H.264/AAC 우선은 이번 실제 경로에서 확인한 MP4 처리와 초기 normalized 출력 목표를 반영한 **동률 정책**이다. codec이 다르면 같은 bitrate가 같은 품질을 뜻하지 않으므로 codec 간 bitrate 비교는 하지 않는다. AV1/VP9가 더 작은 파일을 제공하거나 특정 환경에서 더 적합할 수 있으나 이번 실험에서 객관적 화질을 측정하지 않았다. 실험의 실패가 codec 자체 때문이라고 단정하지 않았다.

더 높은 해상도/FPS를 codec·transport 편의 때문에 낮추지 않는다. 높은 AV1/미확인 FPS 포맷이 선택되면 그 선택을 보존하고 이후 모드 호환성·FPS 확인이 실패할 경우 작업을 실패시킨다. Fast↔Accurate·화질 자동 변경을 추가하지 않았다. HLS 후보도 배제하지 않으므로 CHZZK Best metadata 선택은 hls-8384이며 실제 다운로드 성공은 미확인이다.

모든 자동 선택에 DeclaredQualityOnly를 넣는다. 누락된 ranking 정보는 RankingMetadataIncomplete, metric 동률의 ID 선택은 StableIdTieBreak다. ID가 낮다고 더 좋은 품질이라는 뜻은 아니다. bitrate가 측정값인지 언어/채널/HDR/프로파일이 같은지까지 추정하지 않으며 현재 최소 모델의 비교 한계를 보존한다. 높이 미확인 후보를 비교할 수 없으면 경고하고, 확인된 높이의 video가 하나도 없으면 Best를 실패시킨다.

## 코드·자동 검증

- Application/Validation/FormatSelector: 독립 순수 선택기. 순위는 입력 배열 순서와 무관하며 FPS 교차 곱은 BigInteger로 계산한다. 원격 조회·tool 실행·mode 판단을 호출하지 않는다.
- VideoFormat에 nullable `VideoBitrateKbps`/`AudioBitrateKbps`와 safe `FormatTransport`를 최소 추가했다. source의 동일 해상도 후보·오디오 bitrate가 달랐으므로 필요한 정렬 정보만 더했다. tbr를 영상/음성 bitrate로 추정 대체하지 않는다. protocol 원문 URL을 공개하지 않는다.
- parser는 vbr/abr JSON 숫자만 정확히 표현 가능한 양의 decimal로 보존한다. 96-bit coefficient·scale 범위 밖·0·음수·숫자 문자열은 unknown+필드 진단이다. 거대한 지수에서 큰 거듭제곱을 만들거나 조용히 반올림하지 않는다. 명시 Absent stream의 bitrate는 적용하지 않는다.
- FormatSelection에는 적용 순서 RankingCriteria와 copied read-only 경고를 보존한다. 수동 후보 생성은 criteria가 비어 있고 자동 selector는 순서를 명시한다. FPS 중복 저장은 추가하지 않았다.
- 기존 3b fixture는 보존했다. 새 selection fixtures는 같은 scalar/format allowlist 자료에 이번 실제 vbr/abr/protocol만 추가했으며 title/uploader는 익명이다. 자동 테스트는 네트워크를 사용하지 않는다.
- 현재 fixtures: CHZZK Best hls-8384; YouTube Best 137+140. extractor 순서를 뒤집어도 같고, 필요 조건이 달라지면 결과가 달라질 수 있다. 특정 ID를 제품 코드에 박아 넣지 않았다.
- 최종 `dotnet build ClipMaker.slnx --no-restore`: 경고 0·오류 0.
- 최종 `dotnet test ClipMaker.slnx --no-build --no-restore`: Core 85·Media.Infrastructure 73·Downloader.Infrastructure 292, **450개 전부 통과**. 이후 미선택 후보의 bitrate 누락도 비교 불확실성 경고에 보존하도록 보완했다. 영향받은 Downloader 테스트 **293개가 다시 전부 통과**했고 최종 검증 합계는 **451개**다. 통과한 다른 프로젝트를 불필요하게 반복하지 않았다. 이번 신규 50개는 실제 fixture/order, spatial/temporal 우선순위, Int64를 넘는 FPS 곱, codec/transport/bitrate tie, 누락/중복/unknown stream, silent 오판, ordinal tie, exact decimal 상한과 거대 지수, ranking 기준 불변성을 검증한다.
- 실험 프로그램 첫 빌드는 top-level args 변수명 충돌로 실패했고, 이름 치환이 FFmpeg 인자 이름까지 바꾼 두 번째 빌드도 실패했다. 정확히 수정한 세 번째 빌드가 통과한 뒤 원격 획득을 시작했다. 제품 테스트 실패는 없었으며 실험 빌드 실패·원격/출력 실패를 성공으로 숨기지 않는다.

## 다음 커밋·잔여 조건

권장 커밋 메시지: `feat: Add deterministic video and audio format selection`.

3c-2의 논리적 커밋 지점에서 멈춘다. 4단계 전에 CHZZK 실패 원인의 안전한 진단·구간 획득 경로, VP9 MP4 header 문제, 분리 A/V의 source↔local 시간축·공통 origin과 검증 방식을 정리해야 한다. Fast 허용 codec/획득 조합·Accurate 성공 정책을 이번 codec 우선순위만으로 확정하지 않는다. Phase 1 전체 준비/구현/검증 완료, Phase 2 시작 조건은 아직 충족되지 않았다.
