# CHZZK player 시간 정규화 관찰

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `20b5ba3712b9f40a54beddc11573d0676b506301`, branch `phase/1`, 작업 트리 clean. Phase 1 Base는 유지한다.

## 판정

[playlist·fragment 대조](PHASE_1_CHZZK_SOURCE_ORIGIN_EXPERIMENT.md)에 이어 실제 익명 player 한 세션과 페이지가 로드한 공식 player 코드를 확인했다. player에는 fragment decode 시각에서 playlist 위치를 빼 초기 offset을 구하는 경로가 있다. 두 품질의 실제 buffered 경계는 각각 다른 offset을 적용한 기존 fragment 값과 1µs 이내로 맞았다. **player가 언제나 하나의 품질 독립 원점을 제공한다고 볼 근거는 확보하지 못했다.** 현재 선택 화질의 첫 packet을 제품 원점으로 삼는 정책도 채택하지 않는다.

이번 단위는 실제 player 관찰·코드 분석이다. 직접 runtime initPTS/SourceBuffer timestampOffset을 읽거나 브라우저의 decoded frame/audio를 기존 payload에 연결한 실험은 아니다. 원점을 확정한 trim 성공·4단계 준비 완료로 해석하지 않는다.

## 실제 관찰

computer-use 스킬과 Codex In-app Browser로 기존 video `15430558`을 열었다. 최초 탭 호출이 약 3375초 지연되어 반환됐고 당시 AX에 재생 불가 메시지가 있었다. 추가 media DOM 조회에서는 오류 없이 재생 중이었다. 최초 재생 상태의 currentTime은 6.794296, buffered는 `[0,53.930688]`이었다. UI로 일시 정지한 뒤 초기 구간·중간 구간에서 화질 메뉴를 확인하고 720p→1080p로 바꿨다. 일시 정지 상태에서도 player는 media를 미리 읽었다. 실제 네트워크 전송량이나 지연 동안의 누적 읽기 양은 측정하지 않았다.

| 관찰 상태 | media currentTime | media duration | UI·상태 |
|---|---:|---:|---|
| 초기 일시 정지 | 19.711694 | 2667.563333 | 자동(1080p), paused |
| 720p 중간 탐색 완료 | 93.964544 | 2667.563333 | 720p 확인, readyState 4 |
| 1080p 전환 직후 | 93.964544 | 2667.561333 | readyState 1 |
| 1080p 로드 후 | 93.964544 | 2667.561333 | 1080p 확인, readyState 4, error 없음 |

초기 UI가 자동 1080p를 표시했다고 실제 최초 fragment 품질이나 초기 offset까지 확인된 것은 아니다. 중간 UI slider의 aria-valuenow는 93, 읽기 문구는 1분 34초였다. UI 숫자의 정밀도와 native currentTime 소수 값을 구별한다.

slider의 accessibility setValue는 지원되지 않았다. 좌표 탐색은 83.964544로 이동했다. 연속 두 Right 입력은 한 번의 5초 이동만 반영했고, 추가 Right 입력으로 93.964544에 도달했다. 따라서 이번에 **정확한 IN=91.25 탐색은 수행하지 않았다.** 같은 중간 native currentTime에서 품질 변경을 관찰했다. 영상 element에는 공개 own property가 없었고 initPTS 로그 조회도 0개였다. 브라우저 관찰은 지원된 read-only DOM·UI·로그 API만 사용했다.

## 실제 로드된 코드의 경로

페이지 자산 inventory에서 `player-vendor-B7YwD4RF.js`를 확인한 뒤 공식 정적 script를 실험 폴더에 받아 읽었다. 첫 sandbox 요청은 DNS 오류로 실패했고 승인된 재요청은 성공했다. 패키지·도구를 설치하거나 받은 script를 별도 실행하지 않았다.

크기는 6,637,952 bytes, SHA256은 `FFB936C4BA3747A71D546E3F65F0F262E7DDAF29F2B65D3360E3DC8B0ACFE468`이다. bundle에는 같은 시간 처리 논리를 가진 HLS 경로 두 개가 포함되어 있었다. 이번 세션이 어느 복사본/경로를 사용했는지는 직접 확인하지 못했다. 구체적 파일·character offset은 [증거](evidence/PHASE_1_CHZZK_PLAYER_TIMELINE.json)에 기록한다. [실제 공식 player 자산](https://ssl.pstatic.net/static/nng/glive/resource/p/static/js/player-vendor-B7YwD4RF.js)

관찰한 fMP4 passthrough 경로의 계산을 요약하면 다음과 같다.

- track의 시작은 fragment tfdt의 decode 시각에서 읽는다. PTS와 DTS가 다르면 first video PTS와 동일하지 않다.
- 초기 offset이 없으면 audio/video 중 더 이른 decode 시작 track을 고른다. 초기 offset이 있으면 기존 기준 track을 고려한다.
- 선택 track의 decode 시작에서 playlist timeOffset을 빼 initPTS를 만든다. 정상 같은 연속 구간에서는 기존 offset을 유지하는 경로가 있고 reset·불연속·불일치 조건에서는 다시 계산할 수 있다.
- fragment append에는 initPTS를 음의 시간 offset으로 전달하고 SourceBuffer timestampOffset에 적용하는 경로가 있다.
- video wrapper의 getCurrentTime은 native video.currentTime을 반환한다. 이 사실만으로 앱의 모든 표시/시간 변환 경로가 확인된 것은 아니다.

upstream hls.js의 PassThroughRemuxer에서도 해당 초기 기준 track 선택·offset 유지/계산 구조를 확인했다. upstream master를 실제 배포 버전이라고 간주하거나 이 설명으로 설치된 bundle 관찰을 대체하지 않았다. [hls.js 원본](https://raw.githubusercontent.com/video-dev/hls.js/master/src/remux/passthrough-remuxer.ts)

## 기존 fragment와 buffered 경계의 간접 대조

이전 실험 index 1의 실제 packet 경계를 사용한다. clock은 이번 측정의 48000이며 모든 source 값은 이전 증거에서 검증한 값이다.

| 대조 | source 경계 | 시험 offset | 계산된 browser 경계 | 실제 buffered 경계 |
|---|---:|---:|---:|---:|
| 720p video 첫 PTS | 196792 | 99007 | 2.0371875초 | 2.037188초 |
| 720p audio 마지막 packet end | 291520 | 99007 | 4.0106875초 | 4.010688초 |
| 1080p audio 마지막 packet end | 287423 | 95935 | 3.989333333…초 | 3.989333초 |

값은 `(source 경계 − 시험 offset)/48000`으로 계산했다. 해당 두 offset은 첫 fragment의 audio decode 시작과 같다. 1080p의 실제 buffered 시작 2.005334는 잘린 범위이므로 segment 첫 packet 경계로 사용하지 않았다. buffered는 브라우저가 유지한 재생 가능 범위이며, 그 자체가 특정 payload의 직접 관찰은 아니다.

이 대조는 품질별 offset 모델을 지지한다. 동일 UI currentTime 유지가 동일 source 내용의 유지까지 증명하지는 않는다. 만약 이 두 offset이 각 품질의 전체 presentation에 적용된다면, 이전 공통 AAC raw PTS 차이가 ±1 sample인 상황에서 같은 payload의 browser 시간은 3072±1 sample(약 64ms) 차이가 난다고 예측된다. **이는 조건부 예측이며 이번 브라우저에서 decoded payload를 직접 측정한 결과가 아니다.** 일반화 실험의 반례는 계속 유효하다.

## 다음 작업: 시간 기준 하위 계약 정리

원본 URL만으로 사용자 browser 세션의 초기화·화질 변경 맥락을 그대로 복원할 수 있다는 가정을 두지 않는다. 다음은 추가 다운로드 반복보다 제품의 source 시간 기준과 불확실할 때의 실행 조건을 정리하는 단위다. 이미 채택한 일반 정책을 재승인받는 작업은 아니다.

| 하위 계약 대안 | 얻는 성질 | 남는 비용·제약 |
|---|---|---|
| 선택 포맷의 입력 상대 시간 | 기존 도구 seek와 연결하기 단순함 | 같은 숫자 IN의 내용이 품질마다 달라질 수 있고 임의 browser 세션과 일치 보장 불가 |
| 명시적인 source 기준 timeline을 고정하고 출력 포맷을 그 timeline에 연결 | 품질 변경에도 같은 원본 내용 기준을 유지할 수 있음 | 기준 timeline 선택 근거·mapping·coverage 검증 필요, provider UI와 일치 여부는 별도 |
| 실제 사이트 player 세션 시간을 정확히 재현 | 그 세션의 표시/내용 대응을 목표로 할 수 있음 | URL 외 session 초기 offset·선택 track·reset 맥락이 필요하며 현재 공개 계약/관찰 API로 검증하지 못함 |

품질 독립적인 내용 기준이 요구된다면 두 번째 방향이 요구사항에 더 맞지만, **특정 품질/고정 2초/최소값을 기준으로 확정하지 않았다.** 일반 시간 입력 정책은 유지하고, 이 하위 계약과 보증 범위·실행 거절 조건을 구체화한 뒤 제한된 실험으로 검증한다. 아직 원점 mapping이 검증되지 않은 Accurate 결과를 성공으로 확정하지 않는 조건이 필요하다. 실제 서비스 구현은 그 계약 정리와 관련 선행 검증 다음이다.

## 검증·변경 범위

[scalar 증거](evidence/PHASE_1_CHZZK_PLAYER_TIMELINE.json)에 UI/media 관찰, bundle hash·위치, 경계 계산, 직접 관찰과 조건부 추론을 구분해 남겼다. 인증·서명 URI·header·cookie·raw 페이지/로그·전체 bundle은 커밋하지 않는다. 공개 정적 script URL만 증거에 포함한다. 새 제품 코드·프로젝트·패키지 변경은 없고 제품 빌드/테스트는 반복하지 않았다. JSON·문서 링크·경계 계산·diff 공백 검사를 수행한다. Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다.

이번 논리적 커밋은 결과 문서·증거 1개·설계·baseline 네 파일이다. staging/commit은 수행하지 않았다.
