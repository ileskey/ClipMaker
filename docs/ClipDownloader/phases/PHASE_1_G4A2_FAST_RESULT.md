# Phase 1 G4a-2 — 공통 source 시간축의 Fast copy와 MP4 presentation 끝

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `f34ee6d407495b69cc0ecdace074f7fe4bf3a96d` (`docs: Record Fast copy timeline and boundary counterexamples`), branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **제한된 H.264/AAC Fast 후보의 공통 시간축·coverage·MP4 presentation 끝 대조 완료**. [G4a baseline](PHASE_1_G4A_FAST_RESULT.md)의 A/V 이동량 차이를 보완했고, ffprobe duration과 presentation 끝의 의미를 구별했다. 제품 구현·모든 입력의 Fast 지원·G4 전체/4단계 준비/Phase 완료는 아니다. 이미 채택한 일반 Fast 제한과 [G3c 처리 계약](PHASE_1_G3C_AV_BOUNDARIES_RESULT.md)을 다시 승인받지 않는다.

## 1. 범위·사전 기준·후보

기존 G4a 합성 source/7초 이동군과 G2 CHZZK 1080p prefix만 사용했다. 새 원격 다운로드·도구/패키지 설치·제품 코드/프로젝트 변경은 없다. FFmpeg/ffprobe 8.1, SDK 10.0.302, package-free ignored harness, 기존 ProcessRunner·60초 제한·root/Job 자식/reader 확인을 적용했다.

선행 keyframe K를 source 공통 시작점으로 놓고, stream별 확인된 Bᵢ를 적용해 `output PTS/DTS = local PTS/DTS + Bᵢ − K`로 packet을 이동하는 후보를 정했다. 서로 다른 STARTPTS를 각자 지우지 않는다. 재인코딩 없이 원본 압축 payload를 보존한다. 사전 gate는 일관된 source/output 대응, 두 stream의 동일 이동량, 의미 있는 초기 지연 보존, 요청 target coverage, source video frame 대응, sample table/edit list/decoded 끝 대조였다.

| 입력 | 사용자 target의 source IN/OUT | 공통 keyframe K | local 입력 seek | video/audio 이동 상수 (clock 240000) |
|---|---|---:|---:|---:|
| synthetic | [1.45,3.45) | 1.001 | 1.001 | -240240 / -240240 |
| 7초 이동군 | [8.45,10.45) | 8.001 | 1.001 — F=7에 상대적 | -1920240 / -1920240 |
| CHZZK 1080p | [4475935,4955935)/48000 | 93 | 1.75 | -420000 / -420325 |
| 초기 지연 대조 | [0.01,1.2) | 0 | 0 | 0 / 0 |

CHZZK Bv=4380000/48000, Ba=4379935/48000을 그대로 사용한다. Q=1.75는 확인된 K의 local video 위치다. 이번 K는 audio native sample grid에서도 정확하게 표현 가능하다. 공통 clock 또는 위 offset 숫자는 제품 상수가 아니다.

복사 골격은 `-copyts -ss Q -i input -to E_local -c copy`에 stream별 `setts=time_base=1/240000:prescale=1:pts=PTS+delta:dts=DTS+delta`를 적용하고, `avoid_negative_ts disabled`, movie/video timescale 240000으로 MP4를 기록한다. 기본 edit list·기본 CTS와 `+negative_cts_offsets`를 비교했다. `use_editlist 0`은 대조군이다. [FFmpeg n8.1 setts.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavcodec/bsf/setts.c)의 prescale은 표현식을 평가하기 전에 지정 output time_base로 환산한다. 표현식은 double 평가이므로 이번 작은 정수 범위의 실측을 모든 Int64 값의 exact 계산 보증으로 확대하지 않는다.

## 2. 첫 종료 인자 실패와 보완

첫 후보는 K부터 OUT까지의 길이를 `-t`로 전달했지만, `-copyts` 상태에서 이번 실행의 종료 판정은 보존된 입력 timestamp 축에 연결됐다. synthetic은 intended OUT 3.45 전에 끝났고, 이동군은 K=8.001보다 이른 2.449를 종료 값으로 전달해 stream 없는 MP4가 됐다. copy/probe는 exit 0이었지만 explicit video/audio decode는 exit 1 (`matches no streams`)이었다. 원점과 종료 좌표를 혼동한 후보를 거절했고 파일/실행 기록을 보존했다.

보완은 관측된 입력 축의 종료 위치를 `-to`에 연결했다. synthetic 3.45, 이동군 10.45, CHZZK local video OUT=575935/48000을 사용했다. 이 인자가 packet을 정확한 presentation OUT에 자른다고 추정하지 않고 실제 잔여 packet/frame과 source 위치를 대조했다. CHZZK stream별 local OUT은 다르며 Fast packet 끝의 실제 결과를 별도로 기록한다.

## 3. 공통 시간축·coverage·영상 내용

보완 synthetic 두 CTS 후보는 Tᵥ=Tₐ=1.001, 이동군은 8.001, CHZZK 두 후보는 93, 초기 지연 후보는 0으로 `S=Pout+Tᵢ`가 일치했다. G4a의 synthetic 18.133ms·CHZZK 130/48000초 이동량 차이는 이 후보에서 사라졌다.

- nonempty 10개 사례의 packet을 모두 source payload·PTS와 대응했다. 반복 AAC payload는 동일 offset의 유일 anchor로 위치를 결정했다. 미대응/미해결 packet 0이며 모든 출력 video frame PTS도 source frame에 대응했다.
- 보완된 정상 후보 6개는 사용자 `[IN,OUT)`의 필수 source video frame과 겹치는 audio packet 누락이 0이었다. 초기 target의 원본 음성 시작 전 지연을 media 누락으로 대체하지 않았다. 이는 packet 획득과 video coverage이며 lossy audio sample 값 동일성의 보증은 아니다.
- synthetic 기본/negative CTS·이동군·CHZZK 기본 CTS 대표 4개의 decoded yuv420p 영상 SHA-256은 대응 source frame과 모두 같았다. synthetic 각 76개·CHZZK 615개다. timestamp와 독립적으로 decoded 영상 내용도 대조했다.
- 이동군은 기준군과 같은 source packet indices를 선택하고 두 stream의 이동량이 정확히 7초 증가했다.
- 초기 지연 후보의 video 첫 PTS는 0, audio 첫 PTS는 4976/48000이었다. source에 확인된 지연이 유지됐으며 stream별 첫 시각을 독립적으로 0으로 만들지 않았다. 0.125초 생성 인자를 presentation 시작이라고 대체하지 않았다.

`use_editlist 0`은 두 synthetic 대조에서 video T=224224/240000, audio T=43888/48000으로 공통 이동량을 훼손했다. 이전 audio packet이 표시되기도 했다. 이 옵션을 보완 경로로 채택하지 않는다. negative CTS는 검증된 후보의 PTS·영상 내용·presentation 끝을 바꾸지 않았고, CTTS version/offset과 DTS 표현을 바꿨다. 이번 범위에서는 기본 CTS 후보가 통과하므로 추가 옵션을 필수로 삼을 근거가 없다.

## 4. ffprobe duration과 MP4 표시 끝의 구별

MP4의 `mvhd/tkhd/mdhd/elst/stts/ctts`를 읽었다. decode sample의 누적 DTS(`stts`)와 composition offset(`ctts`)으로 각 sample의 PTS와 끝을 독립 재구성하고, edit의 media time/empty delay를 적용했다. 정상 후보 6개에서 이 모델·edit list 끝·decoded frame 끝이 정확히 일치했다. audio raw decoded tail을 임의로 유효 끝으로 간주하는 일반 규칙을 만들지 않는다. 이번 copy 사례는 해당 검사 값이 일치했으며 G3 priming/padding gate를 유지한다.

| 보완 기본 후보 | 출력 video decode 길이 / ffprobe duration_ts | 출력 video edit 끝 = decoded 끝 | 출력 audio edit 끝 = decoded 끝 |
|---|---:|---:|---:|
| synthetic | 608608/240000 ≈ 2.535867 | 624624/240000 = 2.6026 | 117696/48000 = 2.452 |
| CHZZK | 2460000/240000 = 10.25 | 2460000/240000 = 10.25 | 492864/48000 = 10.268 |

synthetic의 `stts` 합은 76×8008=608608이지만 composition offset을 적용한 edited presentation 끝은 624624다. 기본 CTS의 `elst`는 duration 624624·media time 16016, negative CTS의 `elst`는 같은 duration·media time 0이었다. 양쪽 movie/track 끝도 624624로 일치했다. 따라서 ffprobe stream duration과 decoded 끝이 다른 사실만으로 잘못된 MP4 끝이라고 할 수 없다.

[FFmpeg n8.1 movenc.c](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n8.1/libavformat/movenc.c)는 PTS 범위·decode timing·edit list를 구별하며 presentation 시작의 trim/delay를 edit에 기록한다. 이번 검증은 구현 코드만 믿지 않고 실제 box/sample table과 decode 값을 대조했다. **G4a의 선언 끝 차이는 ffprobe duration 값의 의미를 더 조사해야 한다는 관찰이었으며, 이번 사례에서는 edit 기반 표시 끝이 일관됐다.** G4a에서 A/V 이동량이 달랐던 별도 반례까지 사라지는 것은 아니다.

실제 source video 끝은 synthetic 3.6036, CHZZK 103.25였다. requested OUT 대비 각각 153.6ms·65/48000초 초과다. source audio 끝은 synthetic 3.453, CHZZK 103.268이며 각각 3ms·929/48000초 초과다. 선행 keyframe 때문에 표시 시작도 synthetic 449ms, CHZZK 11935/48000초 먼저다. Fast는 이번 실제 시작/stream별 끝을 안내할 근거를 얻었지만 Accurate의 `[IN,OUT)` 집합과 같다고 설명하지 않는다. video 뒤쪽과 audio 끝이 서로 다른 점도 숨기지 않는다. 사람의 player 재생 확인은 하지 않았다.

## 5. 통합에 넘길 결론·남은 검증

후속 Fast 통합의 근거는 **확인된 선행 keyframe·공통 source 시작·stream별 Bᵢ 적용·입력 종료 좌표 검사·기본 MP4 edit 보존** 후보다. source/output 대응과 target coverage, metadata의 정확한 의미를 적용한 실제 시작/끝 검사를 함께 넘긴다. 실패를 keyframe 제한으로 숨기거나 모드/화질 변경 fallback을 추가하지 않는다. 최종 서비스/오류·표시 계약은 G5에서 통합한다.

이번 성공은 한 synthetic GOP/B-frame 조건과 한 CHZZK 선택 포맷, native audio grid에 정확히 표현 가능한 K에 한정한다. 비정렬 keyframe의 audio timestamp 양자화·다른 codec·분리 source 축·discontinuity·범용 큰 정수 표현의 지원을 확정하지 않는다. 다음은 **G4b YouTube 분리 A/V mapping·VP9 MP4**, 이후 codec/CFR/FPS와 G5 통합이다. 필요한 입력 범위에서 미확인 관계나 표현 불가를 명시 거절하는 gate를 구체화해야 한다. 제품 4단계·전체 준비/Phase 완료는 아직 Pending이다.

## 실제 검증·증거·커밋 지점

39개 도구 호출 중 정상 38개·초기 empty 이동군 decode 실패 1개다. 실패를 포함해 모든 root/Job 자식 종료·reader drain을 확인했고 probe/hash stdout은 완전했다. 첫 실행은 실패 후 중단해 최초 CHZZK 후보 2개는 실행하지 않았다. 보완 실행 6개와 초기 지연 대조 1개를 추가해 총 output 사례는 11개(nonempty 10개/empty 1개)다. 초기 짧은 출력·noedit 대조는 정상 후보의 성공 수에 포함하지 않는다. 정상 보완 후보 6개가 제한된 의미 검증을 통과했다.

기본·종료 좌표 보완·지연/영상 hash 대조 harness 빌드는 모두 경고/오류 0이었다. 별도 PowerShell에서 payload/PTS·frame membership·target coverage·sample table/edit 끝·지연·7초 이동·decoded pixel hash를 재검증했다. 제품 변경이 없어 전체 제품 테스트는 반복하지 않았다. [증거 JSON](evidence/PHASE_1_G4A2_FAST.json)은 앞선 source 증거를 경로/SHA-256으로 참조하고, 계획·실패·안전한 명령·출력 probe/box·대응·pixel hash와 독립 판정을 보존한다. 서명 URL·인증값은 포함하지 않는다. ignored 실험 파일을 보존했다.

이번 논리적 커밋 범위는 결과·증거·G4a 해석 보완·하위 계약안·설계·baseline 6개 파일이다. staging/commit/tag/branch 변경은 하지 않았다. 권장 메시지: `docs: Verify Fast copy source alignment and MP4 presentation ends`.
