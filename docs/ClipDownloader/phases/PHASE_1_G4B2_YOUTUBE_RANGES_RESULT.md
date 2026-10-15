# Phase 1 G4b-2a — YouTube 분리 원본의 초기·SIDX 범위·mapping/coverage

실행일: 2026-10-12 (Asia/Seoul). 시작 HEAD `834ebc1ee6846677acbc527e8371695f78a0f836` (`docs: Verify native Opus timing and PCM preservation`), branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **실제 H.264 137/AAC 140 원본 각각의 초기 presentation·제한 범위 획득·mapping/coverage 대조 완료**. cross-resource 공통 source 시간 관계는 미확인이다. 따라서 G4b-2 전체·모든 YouTube format 지원·4단계 준비·제품 구현·Phase 완료는 아니다. 앞선 merged 파일의 R 재분리와 달리 이번 자료는 현재 제공되는 원격 resource에서 받은 실제 bytes다.

## 1. 사전 입력·범위

기존 사용자 영상 sFXPM_sfJNo를 현재 yt-dlp/Node 의존성 진단 후 재조회했다. 완료 상태·226초, 명시 video 137(H.264 1920×1080·30fps)·audio 140(AAC stereo·44100Hz)을 확인했다. 이 ID는 실험 입력이며 제품 선택기에 박아 넣지 않는다. 조회 성공을 media 접근/시간축 성공으로 취급하지 않는다.

사용자 target은 [91.25,101.25)초다. 첫 계획은 원격 초기 [0,2)·참조 [88,106) probe와 [88,104) 분리 획득이었다. 첫 probe timeout 후 transport를 분리 진단하고 SIDX가 가리키는 초기/참조 fragment를 제한 획득했다. 원본의 확인된 presentation 원점으로 target을 연결하고 packet/hash·정수 PTS·frame/sample coverage·누락/offset 대조를 검사한다. 서로 다른 resource의 0이 같다는 이유만으로 공통 A/V 관계를 확정하지 않는다.

도구는 기존 FFmpeg/ffprobe 8.1·SDK 10.0.302와 설치된 yt-dlp/Node다. info service의 버전/Node≥22 검사와 기존 config/plugin/cache/remote-component 차단·내부 retry 정책을 유지했다. 새로운 도구/패키지·인증/쿠키·제품 코드는 없다. 실제 자료/실험 harness는 ignored `.artifacts/phase1-g4b2-youtube-20261012-01/`에 보존한다. 서명 media URL/header 원문/metadata JSON은 파일이나 증거로 내보내지 않았다.

## 2. 실패와 transport 대조

1. metadata 조회는 성공했지만 FFprobe의 137 초기 원격 probe는 **60초 TimedOut**였다. root/Job 자식/reader 종료를 확인했다. 그 실행에서는 reference·구간 획득을 시작하지 않았다. 관측하지 않은 네트워크 전송량/원인을 추정하지 않는다.
2. 새로운 URL로 HttpClient의 각각 최대 65536-byte prefix Range를 진단했다. 137/140 모두 **206**, 정확한 `Content-Range 0-65535`를 받았다. 반환 total length는 각각 53949513·3658786 bytes였다. media 접근 자체가 불가능하다고 판정하지 않는다.
3. bounded header에서 SIDX를 읽고 원본 fragment의 byte offset/길이/시간을 얻었다. 초기 video fragment의 로컬 probe는 성공했지만 harness의 lazy JSON projection이 disposed document를 참조해 저장에 실패했다. 즉시 materialize하도록 수정하고 기존 media를 보존했다.
4. HTTP Range header 기반 target fetch에서 요청/응답 범위 검사가 실패했다. 당시 actual status를 저장하지 못했으므로 그 요청의 HTTP code를 확정하지 않는다. 불완전한 reference 파일은 보존하고 검증 자료에서 제외했다.
5. 명시 range query와 HTTP Range header를 함께 보낸 512KiB 대조는 유효한 SIDX offset 19426473에서 **416**이었다. offset 0의 header 요청은 206이었다. 이 한 관찰만으로 CDN 내부 원인이나 range 크기 제한을 단정하지 않는다.
6. **range query만 사용**한 별도 대조에서는 HTTP 200·요청 조각과 정확히 같은 Content-Length로 필요한 데이터가 왔다. 요청 chunk는 최대 524288 bytes이고, 길이가 다르거나 예상 response 조건을 충족하지 않으면 body를 읽지 않는다. body를 끝까지 정해진 크기만 읽고 fragment byte 구간의 연속/완전성을 독립 검증했다.

[yt-dlp 2026.08.19 YouTube extractor](https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/yt_dlp/extractor/youtube/_video.py)도 range query로 resource fragment를 만드는 경로를 갖는다. 이 참조와 위 actual response는 query 방식 실험의 근거다. 이를 제품 transport fallback·SIDX downloader 채택으로 확정하지 않는다. 동일 실패를 무조건 반복하는 Application 전체 retry도 추가하지 않았다. 각 진단은 서로 다른 조건이며 새 URL이 필요한 경우 metadata를 별도로 조회하고 기록했다.

## 3. 필요한 원본 fragment만 획득

SIDX의 직접 byte reference만 지원하는 임시 reader로 ftyp/moov 초기화와 선택 fragment의 원본 bytes를 결합했다. original tfdt·compressed payload는 변형하지 않았고, 부분 파일에 원본 전체 byte offset을 가리키는 SIDX는 넣지 않았다. hierarchical index/누락 init/표현 불가 범위는 거절한다. 각 참조 window의 fragment 합계 상한은 8MiB다.

| format | SIDX timescale / earliest | 참조 index | 실제 참조 시간 범위 | 원본 fragment bytes |
|---|---|---|---|---:|
| 137 | 15360 / 0 | 14~17 | [1290240,1658880)/15360 = [84,108)초 | 5649728 |
| 140 | 44100 / 0 | 8~10 | [3522560,4843520)/44100 ≈ [79.877,109.830)초 | 484640 |

요청 [88,106)과 겹치는 fragment를 받았으므로 실제 여유 구간은 video 24초·audio 약 29.954초다. 초기 window 역시 fragment 단위여서 video 첫 6초·audio 첫 약 9.985초를 포함한다. “2초/18초만 전송됐다”고 주장하지 않는다. 전체 영상 resource를 받는 fallback은 사용하지 않았다. 최초 FFprobe 시도의 전송량은 별도 미확인이다.

기존 완료된 초기 video fragment는 SHA-256·새 header bytes와 initial metadata의 일치를 확인한 뒤 재사용했다. 실패한 partial 파일과 새 query-only 결과는 다른 폴더에 보존했고 파일을 덮어쓰지 않았다. 실제 complete byte-range reference를 로컬 FFmpeg로 재mux하여 분리 prefix를 만들고 probe/decode했다.

## 4. 원본 resource 각각의 원점·대응·coverage

137 초기 stream/decoded video 시작은 0/15360이고, 첫 packet PTS 0·DTS -512였다. 140 초기 stream/decoded audio 시작은 0/44100이고 첫 packet PTS 0·1024 samples였다. 첫 raw DTS를 presentation 원점으로 대체하지 않았다. 원본의 실제 초기 bytes와 SIDX earliest 선언을 같이 확인했다. target 파일의 첫 시각을 원본 O로 쓰지도 않았다.

| 검사 | 137 | 140 |
|---|---:|---:|
| reference→prefix anchor packet | 720 | 1290 |
| native time_base | 1/15360 | 1/44100 |
| 일정 source-local offset | 0 | 0 |
| 미대응/미해결 packet | 0 | 0 |
| target과 겹치는 필수 packet | 301 | 432 |
| 누락 packet | 0 | 0 |
| PTS가 target 안인 decoded frame | 300 | 431 |
| target decoded frame PTS 집합 | 동일 | 동일 |

packet/window intersection과 video `[IN,OUT)` frame PTS membership은 다른 숫자다. 140은 IN 직전 시작한 decoded frame의 일부 sample도 필요하므로 PTS가 안에 있는 431 frames만으로 sample coverage를 판정하지 않았다. 실제 native sample target **[4024125,4465125)**에서 합계·union 모두 **441000 samples**, gap 0이며 처음/끝을 모두 덮었다. 이 sample intervals는 원본/reference와 prefix의 frame PTS/nb_samples 대조에 기반한다. lossy AAC decoded 값이 독립 decoder history에서도 bit 같다고 일반화한 것은 아니다.

필수 packet을 하나 제거한 offline 대조와 offset을 1 tick 바꾼 대조는 조건을 충족하지 못했다. copy/probe/decode exit 0와 별도로 packet identity·PTS/time_base·coverage를 검사했다. 실제 clipping/Accurate output·Application 서비스는 구현하지 않았다.

## 5. 공통 source 축은 아직 별도 gate다

이번 두 resource는 같은 영상 metadata에서 선택됐고 각각의 native 시작·SIDX earliest가 0이었다. 각 source-local 관계도 확인했다. 그러나 metadata에는 선택 format의 manifest 관계가 없었으며, **이 두 독립 resource의 cross-track 시간 관계를 확인할 추가 source 근거는 이번에 확보하지 않았다**. 두 원점을 min/0으로 합쳐 공통 O가 확인됐다고 표시하지 않는다.

다음 G4b-2b는 제공되는 통합 A/V 원본 참조 또는 provider manifest timing 관계 등으로 cross-resource 근거를 보완하고 지원 조건을 판단한다. arbitrary browser session 재현이나 품질 독립 시간을 새 요구로 추가하자는 뜻은 아니다. 사용자 채택 A(선택 포맷 시간 의미)는 유지한다. 증거를 확보할 수 없다면 G5에서 최소 지원 경계와 명시 검증 실패 조건을 정리하며, 미확인 A/V 성공을 만들지 않는다.

VP9/Opus 원격 원점/coverage 일반화·codec/CFR/FPS·G5 통합도 남아 있다. 초기/개별 resource 준비·공통 축 준비·제품 구현·Phase 완료를 구별한다. H.264/AAC 개별 gate 통과를 전체 G4b-2 완료로 확장하지 않는다.

## 실제 검증·기록·커밋 지점

증거에 남긴 tool 호출은 **32개**, 정상 Exited 31개·첫 FFprobe TimedOut 1개다. 모두 root/Job 자식 종료·reader drain을 확인했다. HTTP 진단/fragment 요청과 harness 예외는 tool process 결과와 따로 기록했다. JSON 수명 오류·response mismatch·416·경로 변수 범위 CS0103 빌드 실패를 숨기지 않았다. 오류를 수정한 최종 harness 빌드는 경고/오류 0이었다. 제품 변경이 없어 전체 제품 테스트는 반복하지 않았고 사람의 player 재생은 수행하지 않았다.

독립 PowerShell에서 actual Content-Length/chunk byte 연속성·reference SHA-256·각 packet hash/PTS·초기 presentation·target sample union·누락/offset 대조를 검증했다. [증거 JSON](evidence/PHASE_1_G4B2_YOUTUBE_RANGES.json)에 실패/진단·안전한 요청·SIDX reference·HTTP numeric response·원본 reference/분리 prefix probe·독립 판정을 보존했다. 원시 URL·인증값은 포함하지 않는다.

이번 논리적 커밋 대상은 결과·증거·하위 계약안·상세 설계·baseline 5개 파일이다. staging/commit/tag/branch 변경·Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다. 권장 메시지: `docs: Record YouTube indexed source coverage and timing limits`.
