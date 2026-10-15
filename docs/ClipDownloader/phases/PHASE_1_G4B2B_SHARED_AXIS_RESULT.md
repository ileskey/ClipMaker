# Phase 1 G4b-2b — 공통 HLS presentation과 DASH movie 시간 변환 대조

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `9c5b53b0e7da21e931b1fbd87887895adb8120bc` (`docs: Record YouTube indexed source coverage and timing limits`), branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **한 YouTube source의 137/140에 대해 공통 provider presentation 연결·encoded content·reference↔선택 movie 좌표 변환을 제한 검증했다**. [G4b-2a](PHASE_1_G4B2_YOUTUBE_RANGES_RESULT.md)의 “각 시작이 0” 관찰에만 의존하지 않는 근거를 확보했다. media/transport와 movie presentation은 같지 않으며, HLS raw 시간을 사용자 입력 축으로 바꾸지 않는다. 최종 source descriptor/서비스 통합·다른 format·G4 전체·4단계 준비/제품 구현/Phase 완료는 아니다.

## 1. 사전 질문과 실제 참조 선택

선택 출력은 기존 DASH video 137·AAC audio 140이다. 새 metadata 조회에서 통합 A/V 파일 포맷은 없었지만 HLS manifest의 AVC 1080p30 video 270·audio rendition 234가 제공됐다. 추가 rendition은 source 관계를 확인하는 참조이며 화질/모드 fallback이나 출력 선택 변경이 아니다.

사전 gate는 provider의 실제 track grouping, 선택 resource와 참조의 내용 대응, signed PTS/time_base 변환, 원본 movie edit/codec 의미 대조다. 같은 URL의 영상 ID·같은 시작값·codec 이름만으로 공통 축을 확정하지 않는다. 원시 metadata/manifest/서명 URI는 메모리에서만 읽고 allowlist 선언·hash·numeric timestamp만 보존한다.

도구는 기존 FFmpeg/ffprobe 8.1·SDK 10.0.302·yt-dlp/Node다. metadata 의존성/버전 검사를 유지했고 새 설치·인증/cookie import·제품 코드/프로젝트 변경은 없다. ignored `.artifacts/phase1-g4b2b-shared-axis-20261012-01/`에 참조 segment와 실험 자료를 보존한다. 기존 G4b-2a DASH 초기/참조 bytes를 재사용했다.

## 2. provider의 공통 presentation 선언

video/audio가 같은 master manifest를 참조했다. master의 AVC 1920×1080·30fps Variant Stream은 `AUDIO="234"`였고, `EXT-X-MEDIA TYPE=AUDIO GROUP-ID="234"`는 audio rendition 234로 연결됐다. playlist URI의 literal string은 signature/challenge 처리 때문에 서로 달랐으므로 동일 문자열이라고 강제하지 않았다. representation itag와 codec/resolution/group 연결을 확인했다.

[RFC 8216 §6.2.4](https://www.rfc-editor.org/rfc/rfc8216#section-6.2.4)는 master의 variant/alternative rendition을 같은 presentation의 encoding으로 다루며 matching content와 timestamp 관계를 요구한다. 이번 근거는 이 규칙을 provider가 준 실제 group 선언·실제 media에 적용한 결과다. 규격이 있다는 이유만으로 모든 provider 파일을 compliant/synchronized라고 가정하지 않는다.

두 media playlist는 VOD·6초 target duration·38 segments이며 이번 scope에서 encryption/map/byte-range/discontinuity를 적용할 layout은 없었다. 첫 segment 0과 참조 14~17([84,108)초)을 각각 받았다. 각 window는 8MiB, 각 segment는 4MiB 상한이며 모두 충족했다.

| 참조 | 초기 6초 bytes | 참조 24초 bytes |
|---|---:|---:|
| HLS 270 video | 1387064 | 5855072 |
| HLS 234 audio | 98614 | 391599 |

기존 완료된 video 초기 segment는 보존/재사용했고 전체 영상을 받지 않았다. manifest/group 조회와 segment fetch는 metadata/프로세스 검사와 따로 기록했다.

## 3. container 포장과 내용을 구별한 대응

HLS video는 MPEG-TS Annex B, DASH 137은 MP4 AVCC여서 raw packet hash를 그대로 비교하면 framing/SPS/PPS/AUD 차이를 content 차이라고 오판할 수 있다. 임시 reader는 길이 prefix/start code를 읽고 picture VCL NAL을 정규화했다. AAC는 raw source payload와 ADTS header를 제외한 payload를 비교했다. 이 처리를 제품 parser로 구현하지 않았다.

| video 대조 window | 정규화 picture 대응 | explicit packet PTS anchor | 미확인 packet PTS | decoded picture 비교 |
|---|---:|---:|---:|---:|
| 초기 | 180/180 | 179 | 1 | 180개 bit 동일 |
| 참조 | 400/400 | 397 | 3 | 전체 window 720개 bit 동일 |

decoded yuv420p 영상 **900개**의 SHA-256이 같은 순서/내용으로 일치했다. framing 정규화만으로 codec parameter의 영향까지 같다고 가정하지 않고 decoded 값으로 보완했다. explicit packet PTS 576개와 decoded video PTS 모두 일정한 관계였다. 없는 packet PTS 4개는 null로 보존했으며 packet field를 추정 생성하지 않았다.

AAC는 segment raw ADTS를 직접 읽어 초기 259·참조 1034, 합계 **1293 frames**의 payload와 각 segment 안의 native 1024-sample 진행을 DASH 140에 대응했다. 참조 경계에서 같은 source AAC frame이 다시 나타날 수 있으므로 발생 횟수와 source 위치를 혼동하지 않는다. HLS 참조를 편집 입력으로 바꾸거나 그 중복을 제품 sample에 추가하지 않았다.

## 4. packed audio의 실제 timestamp

audio 234는 TS가 아니라 **ID3 + packed AAC**였다. 파일명 `.ts`는 실험용 이름이고 실제 형식을 의미하지 않는다. 일반 AAC demuxer의 time_base 1/28224000·0부터 이어지는 PTS는 ADTS를 읽으며 만든 상대 clock이었다. concat packet `pos`도 segment의 첫 ADTS 위치를 정확히 대신하지 못하는 사례가 있어 native anchor로 쓰지 않았다.

[RFC 8216 §3.4](https://www.rfc-editor.org/rfc/rfc8216#section-3.4)의 packed audio는 segment 첫 sample timestamp를 ID3 PRIV의 `com.apple.streaming.transportStreamTimestamp`로 전달한다. 실제 ID3v2.3 frame을 읽어 33-bit·90kHz timestamp를 추출하고, raw segment 첫 ADTS payload를 별도로 찾아 DASH native sample 위치와 연결했다.

| segment | 실제 ID3 timestamp (1/90000) | 첫 DASH 140 sample PTS (1/44100) |
|---|---:|---:|
| 0 | 0 | 0 |
| 14 | 7560881 | 3704832 |
| 15 | 8100048 | 3969024 |
| 16 | 8641306 | 4234240 |
| 17 | 9180473 | 4498432 |

다섯 값은 모두 `floor(DASH sample PTS × 90000 / 44100)`과 같았다. 이 사례에서 nearest rounding은 일부 anchor와 달랐다. 임의의 고정 ms/sample 허용 오차를 채택한 것이 아니라 실제 integer timestamp projection을 대조했다. 세부 sample 선택에는 이미 확인한 원본 44100Hz native clock을 유지한다.

## 5. media 축과 movie presentation 축

HLS video와 DASH 137의 대응은 `HLS PTS/90000 = DASH movie PTS/15360 + 1/30`이었다. 이 +1/30은 첫 시각을 보고 임의로 더한 값이 아니다. 원본 moov의 **137 elst media_time=512·timescale=15360**과 정확히 같았다. 140에는 해당 edit list가 없었고 audio native sample clock은 ID3 projection과 대응했다.

| 선택 source | 명시 원본 edit | 관측된 참조↔movie 관계 |
|---|---|---|
| 137 | media_time 512/15360 | HLS video media/transport 값에서 3000/90000을 빼면 원본 movie PTS와 동일 |
| 140 | 해당 edit 없음 | ID3는 원본 native sample 시각의 90kHz 정수 projection |

따라서 공통 HLS presentation의 content/media 관계와 선택 resource 각각의 **movie presentation 변환**을 연결할 수 있다. raw HLS media clock을 선택 MP4의 사용자 movie clock과 같다고 해석하면 안 된다. 이미 ffprobe가 edit를 적용한 DASH movie PTS에 +1/30을 다시 더하는 source 보정은 하지 않는다. 반대로 HLS를 movie 축에서 비교할 때 edit를 빠뜨리거나 두 번 빼는 대조는 실패했다.

사용자 채택 A(선택 원본의 확인된 presentation 기준)와 G1의 edit/priming 구별을 유지한다. 이번 선택 MP4 입력은 edit 적용 후 movie presentation이며, 참조 transport/media 축을 사용자 축으로 교체하지 않는다. 동일 숫자가 다른 protocol·quality·player session에서 같은 내용을 보장한다는 새 정책을 추가하지 않았다.

target [91.25,101.25)에서 HLS video를 명시 edit로 movie 축에 환산하면 원본 137의 **300개 picture 집합**과 정확히 같았다. raw HLS 숫자를 바로 사용자 IN/OUT에 적용하면 개수는 같아도 picture 집합이 달랐다. 같은 frame 수만으로 Accurate 검사를 통과시키지 않는 대조다. G4b-2a의 원본 movie target/140의 441000 native sample coverage는 유지한다.

## 6. 판정과 통합에 넘길 조건

한 source·137/140 조합의 공통 presentation/content bridge와 명시 좌표 변환 대조를 완료했다. 이전 “원점이 둘 다 0”보다 강한 근거는 **actual master group → 같은 encoded content → native transport timestamp → 원본 movie edit**의 연결이다. 선택 movie 입력 축을 사용한다는 해석은 기존 채택 정책에 따른다. reference media 축 자체를 새 source 사용자 clock으로 채택한 것이 아니다.

G5에서는 source descriptor에 movie/media/transport/native sample 좌표와 이미 적용된 edit 정보를 구별하고, input PTS에 reference offset을 이중 적용하지 않는 최소 gate를 통합해야 한다. 이번 실험의 임시 HLS/ID3/NAL reader를 범용 engine·제품 download fallback으로 바로 채택하지 않는다. group/content/edit 대응을 확보할 수 없는 다른 입력은 확인된 source 관계라고 표시하지 않는다.

지원 범위는 이 영상·이 포맷 pair·초기/24초 참조 window다. 90kHz projection을 넘는 물리적 연속시간 정확도, 모든 YouTube resource, 실제 browser session/playback 동일성은 보증하지 않는다. 다음은 **G4c codec·CFR/VFR·FPS의 판정 범위와 지원 조건 정리**, 이어 G5의 최소 타입/서비스/오류 통합이다. VP9/Opus 등 다른 원격 pair의 미확인 관계는 지원 경계에 보존한다. 전체 G4/4단계 준비·제품 구현·Phase 완료는 아직 Pending이다.

## 실제 검증·증거·커밋 지점

기록된 ProcessRunner 호출 **31개가 모두 Exited·exit 0**, root/Job 자식/reader 종료와 probe/hash stdout 완전성을 확인했다. 별도의 로컬 raw packet 진단도 수행했다. tool 성공과 packet projection/ID3 layout helper의 거절은 구별한다. 처음 literal URI 비교가 같지 않았던 관찰, missing TS PTS의 KeyNotFoundException, ID3v2.4만 허용하던 reader의 v2.3 거절을 보존했다. CA2014 stackalloc 경고 1개를 수정했고 최종 빌드는 경고/오류 0이었다.

독립 PowerShell에서 provider group 연결·VCL identity·decoded picture 값/시간·raw ADTS/native sample 진행·ID3 bytes/정수 projection·moov edit·target picture 집합·이중/누락 edit 대조를 검증했다. [증거 JSON](evidence/PHASE_1_G4B2B_SHARED_AXIS.json)에 필요 선언·segment hash·codec/PTS 대응·pixel hash·원본 box와 이전 source 증거 SHA-256을 남겼다. 원시 URL·인증값은 없다. 제품 전체 테스트·사람의 player 재생은 수행하지 않았다.

이번 논리적 커밋 대상은 결과·증거·하위 계약안·상세 설계·baseline 5개 파일이다. staging/commit/tag/branch 변경·Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다. 권장 메시지: `docs: Verify shared presentation and source movie timing`.
