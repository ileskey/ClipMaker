# Phase 1 / 3c 잔여 실험 계획

작성일: 2026-10-10 (Asia/Seoul). 상태: **실행 전 계획**. 영상 다운로드를 수행하지 않았다.

후속 상태: 사용자가 짧은 구간 다운로드를 허용하여 아래 다섯 요청을 실행했다. 실제 실패/성공·시간축 관찰·선택 구현은 [3c-2 결과](PHASE_1_STAGE_3C2_RESULT.md)를 따른다. 위 상태와 아래 제한 설명은 계획 작성 당시 기록이다. CHZZK/시간축/전체 Phase 검증 완료를 뜻하지 않는다.

## 필요한 근거와 현재 제한

[설계 제4.22절](PHASE_1_DESIGN.md#422-화질-요청포맷-선택--사용자-채택-완료)과 제7절의 시작 조건은 Best 순위·모드별 코덱 호환성을 실험 후 결정하도록 한다. 3b 메타데이터 성공과 2c encoder/muxer 목록은 실제 짧은 출력의 성공·음성 보존·CFR 증거가 아니다. 최초 사용자 지시의 영상 다운로드 금지는 이후 metadata-only 조회 허용으로 해제되지 않았다.

일반 정책을 다시 승인받을 필요는 없다. 다음 실험의 짧은 영상 구간 다운로드는 현재 허용 범위를 넘어가므로 별도 허용이 필요하다. 이 계획이 승인되기 전에는 외부 영상 데이터 획득·원격 media probe를 실행하지 않는다.

## 입력·실행 범위

3b의 사용자 URL과 익명화 metadata fixture를 입력 후보로 사용한다. 실행 직전 같은 source의 metadata를 재조회하여 ID·완료 상태·길이·아래 format ID 존재·codec/FPS를 확인한다. 사라지거나 달라졌으면 다른 ID를 자동 선택하지 않고 중단한다.

| 대상 | 요청 원본 구간 | 명시 format | 현재 관찰값과 비교 질문 |
|---|---|---|---|
| CHZZK 15430558 | [90,100)초 | hls-3192 | 720p60 H.264/AAC 통합 A/V |
| CHZZK 15430558 | [90,100)초 | hls-8384 | 1080p60 H.264/AAC, 해상도 비교 |
| YouTube sFXPM_sfJNo | [90,100)초 | 137+140 | 1080p30 H.264/AAC 분리 A/V |
| YouTube sFXPM_sfJNo | [90,100)초 | 248+251 | 1080p30 VP9/Opus, 동일 해상도 codec 비교 |
| YouTube sFXPM_sfJNo | [90,100)초 | 399+251 | 1080p30 AV1/Opus, 동일 해상도 codec 비교 |

최대 다섯 번의 10초 요청이다. yt-dlp/FFmpeg가 keyframe·구간 획득 과정에서 읽는 원격 데이터가 정확히 10초 또는 고정 바이트로 제한된다고 보증하지 않는다. 실제 획득 길이·구간 편차·위임 프로세스를 기록한다. 전체 영상 다운로드·다른 품질/모드 fallback은 하지 않는다. 실패한 항목의 반복 실행으로 범위를 확대하지 않는다.

현재 설치 도구를 기존 supervisor로 실행한다. 새 패키지·runtime·remote JS components·cookies·plugins는 설치하거나 가져오지 않는다. YouTube의 Node 최소 설정은 3b를 유지한다. 초기 메타데이터에 protocol이 없으므로 protocol/위임 경로는 재조회 시 Infrastructure 내부에서 검사하며 서명 URL을 공개 결과에 기록하지 않는다.

획득 인자 계획은 다음과 같다. 실제 executable/FFmpeg bin/고유 실험 폴더 절대경로를 확인한 뒤 치환한다. 이 명령은 **아직 실행하지 않았다**.

```text
--ignore-config --no-plugin-dirs --no-cache-dir --no-remote-components
--no-js-runtimes [YouTube만: --js-runtimes node:<확인한 절대경로>]
--no-playlist --retries 2 --extractor-retries 2 --fragment-retries 2
--file-access-retries 0 --abort-on-unavailable-fragments
--ffmpeg-location <확인한 FFmpeg bin>
--format <표의 명시 format> --download-sections *90-100
--no-overwrites --output <고유 실험 root>/outputs/<실험 ID>.%(ext)s
-- <canonical URL>
```

metadata는 120초·probe는 60초·사전 도구 확인은 15초 계약을 유지한다. 다운로드/인코딩에는 채택한 정책에 따라 고정 timeout을 추가하지 않는다. 취소는 Job과 reader의 종료 증거를 확인한 뒤 정리한다. stdout/stderr를 bounded collector로 읽고 민감 로그를 저장하지 않는다.

## 검사·판정과 후속 결정

1. 기존 도구 버전/실행·실험 codec/encoder/muxer를 확인한다. 필요하면 네트워크를 쓰지 않는 짧은 synthetic 영상으로 실패 원인이 설치 기능인지 source 경로인지 분리한다.
2. 획득마다 실제 container·stream 수·codec·FPS/time_base·시작 timestamp·duration·A/V offset을 검사한다. 선언 FPS와 summary의 평균만으로 CFR을 판정하지 않는다. 작은 frame PTS 창을 사용한다.
3. 원본 codec의 MP4 stream copy와 H.264/AAC Accurate 후보 재인코딩을 별도 실험 출력으로 비교한다. 기본 merge/remux를 실험 전에 제품 정책으로 확정하지 않는다. 출력 생성·probe·decode 성공, 음성 존재 유지 여부를 각각 기록한다.
4. 동일 해상도/FPS 후보 사이의 codec 호환성·실제 processing 비용·정보 부족과 실패를 표로 정리한다. 선택 후보 FPS와 audio 조합을 보존할 수 있는지 확인한다. 이번 제한된 입력만으로 모든 source·codec 조합을 허용하지 않는다.
5. Best의 해상도/FPS/codec/audio 우선순위와 동률 처리 후보를 근거·trade-off와 함께 구체화한다. yt-dlp의 원본 배열 순서나 format ID의 사전순을 근거 없이 품질 순위로 채택하지 않는다. 모드 호환성 때문에 화질을 낮추는 정책도 조용히 넣지 않는다.
6. 순위·음성 선택 근거가 정리된 뒤 Application `FormatSelector`를 구현하고 실제 fixture의 순서 변경·동률·미확인 FPS/codec/audio·명시 해상도 없음 테스트로 검증한다. 후보 조합 모델 성공을 clip 실행 준비 완료로 해석하지 않는다.

이 실험은 3c의 순위/호환성 결정 근거를 마련한다. 전체 시간축 대응·Accurate 프레임 정밀도·출력 검증·Phase 1 완료는 별도 필수 검증으로 남는다. 실패/불충분은 그대로 기록하고 추정으로 설계를 확정하지 않는다.
