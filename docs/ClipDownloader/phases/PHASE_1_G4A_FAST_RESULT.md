# Phase 1 G4a — Fast copy의 원본 대응·keyframe·A/V 시간축 대조

실행일: 2026-10-11 (Asia/Seoul). 시작 HEAD `431c641a23d6173559aaf645bd6ca3310d752a10` (`docs: Record AV boundary rules and adopted processing contracts`), branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`로 유지한다.

상태: **G4a 제한된 Fast baseline 측정 완료, 단순 copy의 공통 A/V 이동량·끝 검사 반례 확인**. Fast 지원 경로 확정·제품 구현·G4 전체 완료가 아니다. [G3c 채택 계약 제5절](PHASE_1_G3C_AV_BOUNDARIES_RESULT.md)은 유지한다. Fast의 keyframe 편차와 원점/동기 오류를 같은 제한으로 처리하지 않는다.

## 1. 사전 범위와 판정 기준

새 원격 획득 없이 로컬 합성 입력 및 기존 G2 `c1080-fractional/prefix.mp4`를 사용했다. FFmpeg/ffprobe 8.1, SDK 10.0.302, 기존 ProcessRunner·60초 제한·root/Job 자식 종료·reader drain 확인을 적용했다. 새 도구/패키지 설치·제품 코드·프로젝트 변경은 없다. package-free 임시 harness와 media는 ignored `.artifacts/phase1-g4a-fast-20261011-01/`에 보존한다.

합성 입력은 7초·160×90·30000/1001fps H.264, GOP 30·B-frame 2·scenecut 0, 48kHz mono AAC다. 생성 인자는 audio 입력을 0.125초 지연했지만 실제 source의 audio 선언/첫 decoded 시작은 AAC delay 1024 samples가 앞선 4976/48000초였다. 생성 인자를 그대로 확인된 presentation 시작이라고 대체하지 않았다. source video 시작은 0이고 이번 target은 초기 priming 뒤다. 동일 압축 packet을 7초 이동한 remux 대조군도 만들었다. movie clock은 48000, 실제 video time_base는 1/60000이다. 인코더 설정은 재현용이며 제품 Fast 정책으로 채택하지 않는다.

사전 기준:

- 복사된 video/audio payload hash를 원본 packet과 대응시켜 일정한 signed 시간 이동량을 확인한다. 반복 payload는 여러 일치 anchor가 결정한 동일 이동량으로 유일한 위치를 찾을 때만 대응한다.
- 이전 keyframe packet·negative DTS·discard packet과 실제 decoded 첫 표시 frame을 구별한다.
- requested source IN/OUT, copied packet 범위, decoded frame의 실제 끝, stream/format 선언 끝을 따로 기록한다.
- 같은 source axis의 두 stream을 출력에 연결할 때 공통 이동량이 유지되는지 확인한다. keyframe 제한을 이유로 임의 A/V 이동량 차이를 허용하지 않는다.
- 7초 원점 이동 대조군의 packet 선택과 상대 결과가 동일한지 확인한다. probe/decode exit 0만으로 검증 성공을 선언하지 않는다.

단순 copy 명령 골격은 `-ss Q -i input -t D -map 0:v:0 -map 0:a:0 -c copy -movie_timescale 48000 -f mp4`다. 기본 `avoid_negative_ts`와 명시 `disabled`를 비교했다. `-copyts`·filter·stream별 offset 보정을 넣지 않은 baseline이며 다음 경로의 성공을 추정하지 않는다.

CHZZK 요청은 기존 사용자 `[91.25,101.25)`초다. G2의 확인된 O=95935/48000, Bv=4380000/48000, Ba=4379935/48000을 사용했다. 원본 target IN=4475935/48000, video local IN=95935/48000이며 단순 입력 seek에는 그 값을 전달했다. audio local IN=96000/48000과 다르므로 이 단일 입력 경로가 처음부터 공통 원본 축을 복원한다고 가정하지 않았다. 잘못된 단일 축 사용을 확인하는 실제 baseline 대조다.

## 2. packet 위치와 실제 첫 표시 frame

| 사례 | source 요청 IN | 복사된 첫 video packet의 source PTS | 첫 decoded video의 source PTS | 첫 decoded audio의 대응 source 시각 |
|---|---:|---:|---:|---:|
| unaligned default / disabled | 1.45 | 1.001 | 1.468133333… | 1.45 |
| keyframe-aligned disabled | 1.001 | 1.001 | 1.001 | 1.001 |
| shifted disabled | 8.45 | 8.001 | 8.468133333… | 8.45 |
| CHZZK default / disabled | 4475935/48000 | 93 | 93.25 | 4475870/48000 |

unaligned copy는 첫 video packet PTS -28028·DTS -32032 (1/60000), flags `KD_`로 이전 keyframe을 포함했지만 첫 표시 frame은 PTS 0이었다. “copied 첫 keyframe = 실제 재생 시작”이라는 해석은 맞지 않았다. source frame 44의 위치 1.468133333…가 출력 0에 대응했다.

합성 video packet 76개·audio 119개, keyframe-aligned 62개·98개, CHZZK 615개·482개를 모두 대응했다. 합성 audio는 unaligned/shifted에서 65개 유일 anchor·54개 반복 payload, aligned에서 64개 anchor·34개 반복 payload였다. 후자의 위치는 일관된 integer offset으로 각각 유일하게 해결했다. 미대응/미해결 packet은 0이며 모든 출력 video frame PTS도 대응 source frame 집합에 포함됐다. decode sample 값 동일성까지 증명한 것은 아니다.

## 3. 공통 이동량 검사는 별도 gate다

`source PTS = output PTS + Tᵢ`로 packet 이동량을 확인했다. 비교는 time_base를 적용한 정수 공통 clock으로 수행했다.

| 사례 | video Tᵥ | audio Tₐ | Tᵥ−Tₐ | 공통 source 이동량 |
|---|---:|---:|---:|---|
| unaligned default / disabled | 352352/240000 | 69600/48000 | 4352/240000 ≈ 18.133ms | 미유지 |
| keyframe-aligned disabled | 240240/240000 | 48048/48000 | 0 | 유지 — 이 검사에 한정 |
| shifted disabled | 2032352/240000 | 405600/48000 | 4352/240000 | 미유지 |
| CHZZK default / disabled | 4476000/48000 | 4475870/48000 | 130/48000 ≈ 2.708ms | 미유지 |

비정렬 합성에서 source의 서로 다른 시각에 있는 video/audio가 출력 PTS 0으로 연결됐다. 이를 원본에 있던 관계가 그대로 보존된 결과로 판정하지 않는다. CHZZK에는 기존 local Bv/Ba 차이 65/48000초와 이번 local→output video/audio 이동량 차이 65/48000초가 함께 관찰된다. G2의 확인된 관계를 무시한 단순 copy는 source 공통 이동량을 유지하지 못했다.

`avoid_negative_ts disabled` 대조는 두 반례의 packet 선택·이동량을 바꾸지 않았다. negative timestamp 옵션 하나만으로 고쳐졌다고 할 수 없다. 7초 이동군은 기준군과 같은 source packet indices를 선택했고 두 stream의 Tᵢ가 각각 정확히 7초 증가했다. 이 사례의 비정렬 차이는 원점이 0인 파일에서만 나타나는 현상이 아니었다.

## 4. 표시 끝과 metadata 끝도 같다고 가정할 수 없다

| 사례 | 요청 source OUT | mapped decoded video 마지막 frame 끝 | 출력 decoded video 끝 | 출력 video 선언 끝 |
|---|---:|---:|---:|---:|
| unaligned disabled | 3.45 | 3.6036 | 128128/60000 ≈ 2.135467 | 129216/60000 = 2.1536 |
| keyframe-aligned disabled | 3.001 | 3.136466666… | 128128/60000 ≈ 2.135467 | 124124/60000 ≈ 2.068733 |
| CHZZK disabled | 4955935/48000 | 103.25 | 10 | 120016/12000 ≈ 10.001333 |

default 대조의 관찰도 해당 disabled 사례와 같았다. synthetic은 마지막 decoded video PTS 126126, duration 2002 (1/60000)였다. source OUT 초과는 unaligned 153.6ms, aligned 약 135.467ms다. CHZZK의 source video 표시 끝 초과는 65/48000초로 G3c의 해당 source 마지막 frame 끝과 일관된다.

특히 keyframe 정렬은 공통 이동량 검사에는 통과했지만 video 선언 끝이 decoded frame 끝보다 4004/60000초 이른 별도 반례였다. 이 결과를 staging 전체 검사 통과로 표시하지 않는다. 어떤 player가 어떤 끝을 표시하는지까지 이번에 실재생 검증하지 않았으며, metadata 또는 decoded 값 하나만으로 최종 재생 종료를 확정하지 않는다.

audio declared end는 unaligned 96144/48000, aligned 96192/48000, CHZZK 480994/48000이었다. priming/discard·sample·metadata를 구별하는 G3 계약은 유지한다. raw decoded 끝을 유효 audio 끝으로 대체하거나 Fast라는 이유로 sample 보정을 수행하지 않았다.

## 5. 판정·다음 단위

이번 측정은 **단순 copy 경로의 검증 부족을 구체화했다**. 모든 미디어 생성/probe/decode 실행은 성공했지만 unaligned synthetic과 CHZZK naive 경로는 공통 source A/V 관계를 보존하지 못했다. aligned synthetic의 공통 이동량만으로 끝·파일 전체 검증까지 성공이라고 할 수 없다.

다음 논리적 단위 G4a-2는 공통 source 축과 확인된 keyframe을 사용한 copy 후보, stream offset 보존, MP4 edit/선언 끝과 decoded 끝을 대조한다. 원본 관계가 유지되고 출력 끝의 의미가 확인된 후보만 후속 Fast 계약 통합 대상으로 삼는다. 새로운 arbitrary tolerance·STARTPTS 독립 초기화·Accurate fallback·화질 변경은 추가하지 않는다. 이후 G4b YouTube 분리 A/V 관계·VP9 MP4 경로, codec/CFR/FPS 판정과 G5 타입/오류 통합이 남아 있다. 전체 4단계 준비/구현·Phase 1 완료는 Pending이다.

## 검증·기록·커밋 지점

23개 도구 호출 모두 exit 0, root/descendant 종료·reader drain, probe stdout 완전성을 확인했다. 별도 PowerShell 대조로 모든 payload hash·원본/출력 PTS 이동량·video frame source membership·7초 이동군 선택 동일성을 다시 검증했다. [JSON evidence](evidence/PHASE_1_G4A_FAST.json)에 계획·실행 상태·안전한 명령·원본/출력 packet/frame·metadata·대응·끝 대조를 보존한다. 서명 URL/인증값은 포함하지 않는다.

첫 `--no-restore` harness 빌드는 assets 누락 NETSDK1004로 실패했다. 로컬 cache만 사용한 sandbox 빌드는 구체 진단 없이 종료했고 sandbox 밖에서 재실행했다. 다중 var 선언 CS0819를 수정한 뒤 기본·반복 payload 분석 보완 빌드는 최종 경고/오류 0이었다. 새 패키지를 설치하지 않았고 기존 로컬 cache만 사용했다. 제품 코드 변경이 없어 제품 전체 테스트는 반복하지 않았다. probe/decode만 수행했으며 사람의 영상/음성 재생 확인은 수행하지 않았다.

이번 논리적 커밋 대상은 결과·증거·하위 계약안·설계·baseline 5개 파일이다. staging/commit/tag/branch 변경·Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다. 권장 메시지: `docs: Record Fast copy timeline and boundary counterexamples`.
