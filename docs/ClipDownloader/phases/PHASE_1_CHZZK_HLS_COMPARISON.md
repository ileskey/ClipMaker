# Phase 1 — CHZZK HLS 단일 옵션 대조 실험

수행일: 2026-10-10 (Asia/Seoul).

상태: **720p·1080p의 제한된 구간 획득·probe·decode 통과, 정밀 구간/시간축/A/V 동기 검증 미완료**. 제품 획득 서비스나 전역 옵션에 적용하지 않았다. Phase 1 전체 완료·4단계 전체 준비 완료를 선언하지 않는다.

후속 [1080p 음성 coverage 추적](PHASE_1_CHZZK_AV_TIMING_DIAGNOSIS.md): 원본과 전체 packet hash/PTS를 대조하여 남은 음성의 대응은 보존되고 시작 부분 원본 음성 packet 47개는 획득 파일에 없음을 확인했다. 약 1초의 시작 차이를 전체 음성의 지연이나 edit list만의 문제로 해석하지 않는다. 여유 구간 획득 대조와 정확한 로컬 처리 검증은 아직 남아 있다.

## 시작과 대조 조건

- branch `phase/1`, HEAD `0c853b779c9a9e2df31d72cef0ab30b871de1aa1`. 이전 [로그 진단](PHASE_1_CHZZK_HLS_DIAGNOSIS.md)과 증거 파일의 미커밋·미추적 변경을 보존했다. Git staging/commit/tag/branch 작업은 하지 않았다.
- 사용자 지시 "계속해서 실험 진행하자"에 따라 실패한 같은 source `15430558`·hls-3192·[90,100)초 요청의 입력 옵션을 대조했다. 720p 통과 후 실제 Best인 hls-8384도 같은 구간·옵션으로 한 번 확인했다. 다른 포맷으로 자동 fallback하지 않았다.
- 기능상 변경은 `--downloader-args ffmpeg_i1:-extension_picky 0` 하나다. `--verbose`는 인자 위치/진단 관찰용으로만 추가했다. 기존 retry, config/plugin/cache/remote component 차단·명시 FFmpeg bin을 유지했다. `allowed_extensions ALL`이나 전역 config를 추가하지 않았다.
- 각 획득 전에 source Completed·duration 2667초·명시 ID 존재를 기존 metadata service로 재확인했다. 출력은 새로운 ignored 실험 폴더를 사용해 기존 파일을 덮어쓰지 않았다.
- [yt-dlp 2026.08.19 공식 FFmpeg downloader 소스](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/yt_dlp/downloader/external.py#L465)의 입력별 configuration 전달을 확인했다. `ffmpeg_i1`은 첫 번째 입력의 `-i` 앞에 전달된다. 입력 옵션을 출력/postprocessor 옵션에 넣지 않았다.

## 실제 결과

| 항목 | 기본 설정 재현 | 옵션 변경 720p | 옵션 변경 1080p |
|---|---|---|---|
| format | hls-3192 | hls-3192 | hls-8384 |
| 요청 원본 구간 | [90,100)초 | [90,100)초 | [90,100)초 |
| yt-dlp exit | 1 | 0 | 0 |
| 획득 실행 시간 | 이전 기록 참조 | 7.588초 | 9.377초 |
| 최종 영상 파일 | 없음 | MP4 4,672,054 bytes | MP4 10,232,083 bytes |
| video/audio | 첫 segment 검사에서 실패 | H.264 1280×720 / AAC | H.264 1920×1080 / AAC |
| video start / duration | — | 0 / 10.037초 | 0.001 / 10.000초 |
| audio start / duration | — | 0 / 10.011초 | 1.008 / 9.002688초 |
| format duration | — | 10.037초 | 10.009688초 |
| 출력 summary FPS | — | r/avg 60/1 | r/avg 60/1 |
| video time_base | — | 1/12000 | 1/12000 |
| summary probe / decode | — | 둘 다 exit 0 | 둘 다 exit 0 |

원격 명령과 supervisor로 실행한 로컬 probe/decode에서 root·자식·reader 종료를 확인했다. 원격 stdout/stderr는 양쪽 모두 truncated=false였다. verbose stderr는 각각 135,167/134,809 bytes로 기존 256KiB tail 안에 들어왔다. 로그는 메모리에서 URL/인증·서명 관련 정보를 제거한 후 저장했으며 원시 JSON·서명 URL·쿠키를 저장하지 않았다. 전체 manifest 정보가 로그에 출력됐다는 사실은 전체 영상 미디어를 다운로드했다는 의미가 아니다. 실제 명령의 구간 요청은 유지했다.

### 인자 위치 검사기의 오류와 보완

720p 프로그램의 `inputOptionBeforeInput=false`는 원래 증거에 그대로 보존했다. 최초 검사기가 `ffmpeg command line`의 첫 행만 읽었으나 FFmpeg HTTP headers가 여러 줄이어서 옵션은 이어지는 행에 있었다. 정규화한 안전한 로그에서 `-extension_picky 0 -i [REDACTED_URL]` 순서를 다시 확인했고 별도 ArgumentPlacement 증거를 추가했다. false를 옵션 미전달로 해석하거나 원래 결과를 성공값으로 덮어쓰지 않았다.

1080p에서는 다중 행을 포함하는 실제 인자 순서 검사로 수정하여 true를 기록했다. 이 위치 확인을 위해 추가 원격 다운로드는 하지 않았다.

## 전체 로컬 PTS 검사

최초 짧은 검사에는 120-packet read budget을 사용했다. 그것이 영상 120프레임을 보증하지는 않으며 실제 720p에서는 62개, 1080p에서는 120개 영상 PTS를 관찰했다. 이후 추가 다운로드 없이 각 약 10초 로컬 파일의 **전체 decoded video PTS**를 60초 timeout·32MiB full collector로 읽었다. 모든 영상 프레임에서 PTS가 있었고 비증가 PTS는 없었다.

| 관찰 | 720p | 1080p |
|---|---|---|
| decoded frame 수 / PTS 수 | 601 / 601 | 600 / 600 |
| 첫 PTS | 0 | 12 |
| 마지막 PTS | 120200 | 119808 |
| 간격 분포 | 200 ticks × 599, 400 ticks × 1 | 192 ticks × 200, 204 ticks × 399 |
| time_base 적용 | 보통 1/60초, 마지막에 1/30초 간격 한 번 | 16ms·17ms 간격 혼합 |

720p 앞부분에는 모두 200 ticks였으나 끝에서 400 ticks가 있었다. summary 60/1과 앞부분만으로 획득 결과 전체의 균일 간격을 확정하면 놓치는 사례다. 1080p는 1ms 단위로 양자화된 60fps 계열 간격과 양립할 수 있지만 이 관찰만으로 원본 CFR/VFR을 확정하지 않는다. 입력 FFmpeg debug의 약 59.94fps 표시와 출력 summary 60/1도 서로 다른 관찰값으로 보존한다. metadata FPS를 임의로 바꾸거나 60000/1001로 추정 교정하지 않았다.

1080p의 audio start−video start는 약 **1.007초**다. decode가 성공해도 동기 성공이 아니다. 720p의 두 stream start=0 역시 콘텐츠 수준의 A/V 동기·정확한 원본 시작 프레임을 입증하지 않는다. 원본↔획득 시간 대응과 음성 시작 차이의 발생 지점은 별도 검증해야 한다.

## 판정과 적용 범위

- 기본 `extension_picky=true`에서 재현한 검사 실패는 두 명시 포맷에 `extension_picky=0`을 입력별로 전달했을 때 발생하지 않고 획득이 완료됐다. 이는 **현재 도구·source·format·구간의 대조 결과**다.
- 앞선 진단과 이번 결과는 HLS 확장자/감지 형식 일치 검사가 현재 획득 실패의 직접적인 차단 지점이었다는 판단을 뒷받침한다. 서버 naming의 상세 문제나 모든 CHZZK 입력·FFmpeg 버전의 호환성까지 확정하지 않는다.
- 입력 검사를 완화한 실험이므로 제품에서 전역 비활성화로 옮기지 않는다. 향후 적용한다면 관찰한 provider/HLS/도구 조건과 입력 검증 범위를 명시해야 한다. 자동 화질·모드 변경은 하지 않는다.
- 이번 결과를 Fast/Accurate의 정밀 구간·CFR·출력 동기 통과로 분류하지 않는다. 4단계 계약 전에 PTS gap·A/V offset·공통 origin·원본↔로컬 대응을 정리해야 한다. YouTube VP9 header·분리 A/V 문제도 그대로 남아 있다.

## 산출물과 검증 범위

[합친 JSON 증거](evidence/PHASE_1_CHZZK_HLS_COMPARISON.json)에는 baseline, 종료·크기·summary·decode·원래 위치 검사와 보완·window/full PTS·전체 scalar PTS를 포함했다. [720p 안전한 로그 발췌](evidence/PHASE_1_CHZZK_HLS_COMPARISON.720.stderr.txt), [1080p 발췌](evidence/PHASE_1_CHZZK_HLS_COMPARISON.1080.stderr.txt)는 인자·스트림·종료 관찰만 남겼다. 원시 URL/인증 정보·전체 민감 JSON은 없다.

실험 프로그램·생성 미디어는 `.artifacts/phase1-chzzk-extension-compare-20261010-01/`와 `.artifacts/phase1-chzzk-extension-1080-20261010-01/`에 보존했다. 새 네트워크 요청 없이 후속 검사에 사용할 수 있고 Git 커밋 대상에서 제외된다.

제품 코드·솔루션·패키지/도구 설치·CLI/WPF·Phase 2·ClipEditor·Git 조작은 하지 않았다. 두 원격 구간 요청과 로컬 검사만 수행했다. 임시 프로그램의 최종 빌드는 경고/오류 0이다. 전체 로컬 PTS 모드 추가 시 기존 `frames` 변수와 이름이 충돌해 빌드 한 번이 실패했고 `fullFrames`로 수정한 후 통과했다. 이미 통과한 제품 테스트 451개는 제품 변경이 없어 다시 실행하지 않았다.

이 단위는 로그 진단에 이어 대조 실험 결과를 기록하는 커밋 지점이다. 미커밋 진단 자료를 함께 포함할 경우 권장 메시지: `docs: Record CHZZK HLS diagnosis and option comparison`.
