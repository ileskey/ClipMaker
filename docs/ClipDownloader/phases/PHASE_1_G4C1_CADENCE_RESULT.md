# Phase 1 G4c-1 CFR/VFR·FPS와 movie quantization 대조

작성일: 2026-10-12 (Asia/Seoul). 시작 HEAD `90cfbae5b7a40be0b0556e8f1546bc0a6912d70a`, branch `phase/1`, 작업 트리 clean. Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **G4c-1 제한된 cadence 대조 완료**. G4c 전체·지원 codec 확정·G5 계약 통합·4단계 제품 구현·전체 준비/Phase 완료는 아니다. [일반 설계](PHASE_1_DESIGN.md), [채택 시간 계약](PHASE_1_SOURCE_TIMELINE_CONTRACT_DRAFT.md), [evidence](evidence/PHASE_1_G4C1_CADENCE.json)를 함께 읽는다.

## 1. 범위와 사전 기준

새 원격 다운로드·설치 없이 기존 FFmpeg 8.1/ProcessRunner와 G2/G4b-2 원본 기록을 사용했다. 임시 하네스·미디어는 ignored `.artifacts/phase1-g4c1-cadence-20261012-01`에 보존한다. 제품 프로젝트/소스는 변경하지 않았다.

이번 단위는 runbook L1~L4의 **video cadence 질문만** 분리했다. 160×90 FFV1/NUT, 영상만 사용했으며 marker/PCM/A/V 교정·값 비교·full E1 정확도 실험은 수행하지 않았다. 생성 전 plan에 count·각 frame PTS·target `[17.25,27.75)`·passthrough를 고정했다. VFR은 입력 30초 900 frames의 첫 600개를 30fps, 이후 300개를 15fps로 배치한다. 40초의 끝 프레임 duration까지 독립 검증한 것은 아니다.

필수 PTS 존재·엄격 증가·필요 구간 coverage를 먼저 검사한다. FPS `a/b`, time_base `t/u`, frame index `i`에서 `D=t*a`, `N=u*b`, `z_i=PTS_i*D-i*N`을 계산한다. 양자화 폭 `q` ticks의 lattice와 양립하려면 공통 phase interval의 교집합이 있어야 한다: `max(z)-min(z) <= q*D`. 임의의 몇-frame 오차를 허용하지 않는다. 작은 실험 정수 범위에서 decimal scaled arithmetic을 사용했으며 제품의 임의 Int64/BigInteger 구현을 검증한 것은 아니다.

기본 비교는 native tick `q=1`이다. 이는 bounded timestamp lattice 적합성 검사이며 그 자체로 물리적 CFR·전체 영상 CFR·프레임 번호를 증명하지 않는다. 더 거친 upstream clock은 근거 없이 허용 폭으로 채택하지 않는다. CHZZK `q=12`(local 1/12000에서 1ms)는 별도 **가설 대조**다.

## 2. 알려진 cadence와 VFR 반례

| 입력 | 실제 frames | native PTS delta (1/90000) | 선언 r / avg | target frames | precise 출력 PTS |
|---|---:|---|---|---:|---|
| CFR 30 | 1200 | 3000 | 30/1 / 30/1 | 315 | 전체 일치 |
| CFR 30000/1001 | 1199 | 3003 | 30000/1001 / 30000/1001 | 315 | 전체 일치 |
| CFR 60 | 2400 | 1500 | 60/1 / 60/1 | 630 | 전체 일치 |
| VFR 30→15 | 900 | 3000→6000 | **30/1 / 30/1** | 199 | 전체 일치 |

생성 count와 **모든** PTS는 사전 수식과 일치했다. VFR 입력에서 r과 avg가 같아도 실제 PTS 간격은 바뀐다. 평균 FPS를 실제 frame index로 간주하거나 이 두 필드의 일치로 CFR를 단정하면 거짓 양성이다.

정수 목록 통제군에서 frame 하나 제거·동일 PTS 하나 중복·30000/1001을 30으로 치환한 후보를 모두 거절했다. 생성 의도를 알고 있는 60fps→1ms nearest-round 통제군은 16/17ms deltas와 phase span 2/3 tick으로 적합했다. 이 통제군은 **미디어 생성이 아닌 정수 수식 대조**다. 누락 PTS는 실패/미확인으로 남기는 판정 규칙이며 새 corrupt-media 실행 사례는 아니다.

## 3. 기본 MP4 edit quantization 반례와 보완 후보

동일 source PTS membership·`setpts=PTS-17.25/TB`·`-fps_mode passthrough`·`-enc_time_base 1:90000`·H.264·video track timescale 90000을 사용했다. 기본 movie 설정과 `-movie_timescale 90000`만 다르게 실행했다. `-r`/fps 필터는 사용하지 않았다.

| 입력 | 기본 movie 출력의 모든 PTS 잔여 | movie 90000 출력 |
|---|---|---|
| CFR30 / VFR | -60/90000초 (-0.667ms) | 잔여 0 |
| CFR30000/1001 | -51/90000초 (-0.567ms) | 잔여 0 |
| CFR60 | 0 | 잔여 0 |

box 판독으로 기본 `mvhd=1000`, precise `mvhd=90000`, 양쪽 `mdhd=90000`과 elst를 확인했다. CFR30/VFR 기본 empty edit 16/1000은 의도한 1500/90000을 잘라 표현한다. 소수 FPS 기본 후보는 51/90000 첫 표시 차이를 잃는다. **decode 성공·같은 FPS·같은 frame count라도 정확한 표시 PTS 성공은 아니다.** 기본 후보 3개는 의미상 거절하며 보존했다.

precise 후보 4개는 source `[IN,OUT)` PTS 목록에서 공통 IN을 뺀 목록과 출력 PTS가 모두 같고 decode도 성공했다. 이는 VFR에서도 실제 PTS membership을 보존하는 후보 경로를 지지한다. H.264 영상 값의 동일성·실제 표시 끝·audio 동기/priming까지 증명하지 않았다. movie timescale 90000을 모든 입력의 고정 제품 정책으로 채택하지 않는다. G3c의 native clocks와 공통 shift·end 표현 조건을 함께 만족해야 한다.

## 4. 기존 사이트의 제한된 관찰

G4b-2 YouTube137 초기 180 frames/중간 reference 720 frames는 native 1/15360에서 delta512, phase span0으로 30/1 lattice와 일치했다. 이 900개 밖의 source CFR·다른 선택 포맷은 미확인이다.

G2 CHZZK decoded prefix의 **local** video time_base는 1/12000이며 source demux 1/6000을 그대로 적용하면 안 된다. 처음 분석에서 이 단위 혼동으로 비정상 span이 나와 local probe와 대조 후 수정했다. 수정 전 판정을 채택하지 않았다.

| CHZZK | 전체 prefix | target 600 frames | 판단 |
|---|---|---|---|
| 1080p | 840 frames, delta192/204, phase span8 ticks | 같은 delta·span8 | native 1-tick 기준 부적합; 1ms 가설과 양립하지만 upstream quantizer 미증명 |
| 720p | 841 frames, delta200 외에 delta600 하나, phase span400 ticks | delta200, phase span0 | target은 60fps lattice 일치; prefix 전체 연속 CFR 판정은 거절 |

일정하지 않은 1080p PTS만 보고 물리적 VFR로 단정하지 않는다. 반대로 1ms 가설을 실제 quantizer 계약으로 확정하지 않는다. 720p target 밖 gap이 있다는 사실과 target coverage/cadence를 구별한다. source 위치 관계와 coverage는 기존 G2 증거를 재사용했고 이번에 다시 다운로드하지 않았다.

## 5. 계약 통합에 전달할 경계와 남은 작업

- metadata FPS는 기존 시간 변환/선택 정책에 사용하되 declared FPS, inspected cadence와 검사 창을 구별한다. 실제 frame index 보장은 추가 근거가 필요하다.
- CFR 미확인/VFR를 임의 30fps로 fallback하거나 frame drop/duplicate하여 성공 처리하지 않는다. 채택한 G3c source PTS `[IN,OUT)` 정책은 그대로 유지한다.
- VFR이라는 이유만으로 Accurate를 무조건 거절해야 한다는 근거도 없다. 이번 candidate는 membership을 보존했지만 full audio/end/source mapping gate를 아직 통합하지 않았다. 최종 성공·거절 조건은 G5에서 함께 정리한다.
- 검사 창, 원본/native/local clock, declared rational FPS, cadence verdict/미확인 이유를 descriptor에 표현하는 후보를 G5에 전달한다. 새로운 public 모델·제품 구현은 아직 없다.
- 다음 논리 단위 **G4c-2**: 기존 H.264/AAC·VP9/Opus·AV1/Opus 결과와 현재 tool 출력의 codec/container 조건을 정리하고, 제한된 Fast/Accurate 지원·거절 경계를 대조한다. 이후 **G5** source descriptor·error/API 계약 통합. G4c 전체는 아직 완료하지 않았다.

## 6. 실행·검증과 커밋 경계

ProcessRunner 호출 33개 모두 Exited/exit0, root/descendant exit와 stream drain/full stdout을 확인했다. 의미상 기본 movie 후보 3개 거절은 process exit 실패와 구별한다. 최초 csproj 오타로 빌드 오류1개, sandbox 복원 실패(exit1, 표시 오류0개)를 보존·수정했고 기존 로컬 cache source만 사용한 외부 빌드로 진행했다. 최종 하네스 빌드는 경고0/오류0이었다. PTS assertion의 기본 movie 거절과 local/source clock 혼동도 위에 기록했다.

제품 변경이 없어 기존 451개 테스트를 이번 실행 결과로 재보고하지 않는다. 이번 검증은 실제 생성/변환/probe/decode·정수 통제·box 판독·evidence 독립 대조·문서 정합 검사다. 결과/evidence·설계·하위 계약·baseline만 커밋 대상으로 남기며 staging/commit은 수행하지 않는다.
