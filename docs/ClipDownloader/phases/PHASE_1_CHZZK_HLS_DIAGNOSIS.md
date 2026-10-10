# Phase 1 — CHZZK 구간 획득 실패 로그 진단

수행일: 2026-10-10 (Asia/Seoul).

상태: **기존 실패의 직접적인 검사 원인 확인, 수정·성공 재검증 전**. 제품 코드를 변경하거나 CHZZK 획득 성공·4단계 준비 완료를 선언하지 않는다.

후속 실제 상태: 사용자가 실험 계속 진행을 요청하여 [단일 옵션 대조](PHASE_1_CHZZK_HLS_COMPARISON.md)를 수행했다. 720p·1080p의 제한된 획득/probe/decode는 통과했지만 정밀 구간·전체 PTS·A/V 동기 문제는 남아 있다. 위 상태와 아래 실행 전 설명은 최초 진단 시점 기록이다.

## 시작과 기존 기록

- branch `phase/1`, 시작 HEAD `0c853b779c9a9e2df31d72cef0ab30b871de1aa1`: `feat: Add deterministic video and audio format selection`. 작업 트리는 깨끗했고 3c-2 커밋을 확인했다.
- 사용자가 치지직 문제 진단과 오류 메시지/로그 확인을 요청했다. 이전 실험 자료에는 종료 코드·stderr byte 수·제한된 오류 flags만 있었고 구체적인 stderr 문구는 남지 않았다. 당시 CHZZK 실패 원인을 확정할 수 없었던 제한을 유지한다.
- 이전 720p/1080p 두 실패를 모두 반복하지 않고, `hls-3192`의 같은 [90,100)초 요청 한 번으로 재현했다. 기존 output에 덮어쓰지 않도록 새 ignored 실험 폴더를 사용했다. 다른 포맷·구간·전체 다운로드로 대체하지 않았다.

## 실제 재현

- 기존 YtDlpVideoInfoService와 ProcessRunner로 source `15430558`·Completed·duration 2667초·format hls-3192 존재를 먼저 확인했다.
- yt-dlp 2026.08.19·FFmpeg 8.1, 기존 config/plugin/cache/remote component 차단·retry·명시 FFmpeg bin을 유지했다. `--format hls-3192 --download-sections *90-100 --no-overwrites`를 실행했다. 실패를 우회하는 입력 옵션은 넣지 않았다.
- 결과: yt-dlp exit **1**, root·descendant·reader 종료 확인. stdout **401 bytes**, stderr **2244 bytes**, 양쪽 모두 truncated=false. 최종 영상 파일은 생성되지 않았다. 전체 원시 로그나 서명 URL은 저장하지 않았다.
- 로그는 bounded 메모리에서 URL/인증·서명 관련 줄을 제거한 후 파일·화면에 출력했다. JSON 메타데이터 원문을 로그로 출력하지 않았다.
- 재현 자료: [안전한 stdout](evidence/PHASE_1_CHZZK_HLS_DIAGNOSIS.stdout.txt), [안전한 stderr](evidence/PHASE_1_CHZZK_HLS_DIAGNOSIS.stderr.txt), [종료·크기 증거](evidence/PHASE_1_CHZZK_HLS_DIAGNOSIS.json). 임시 프로그램은 `.artifacts/phase1-chzzk-diagnosis-20261010-01/`에 있으며 솔루션/커밋 대상이 아니다.

## 직접 확인한 오류와 해석

stdout은 영상 정보·m3u8 조회와 지정 포맷/구간 선택까지 진행했다. FFmpeg의 핵심 오류는 다음과 같다. URL은 제거했다.

```text
detected format mov,mp4,m4a,3gp,3g2,mj2 ... mismatches allowed extensions in url [REDACTED_URL]
Error when loading first segment '[REDACTED_URL]'
Error opening input: Invalid data found when processing input
ERROR: ffmpeg exited with code 3199971767
```

**현재 재현에서 FFmpeg의 HLS 세그먼트 확장자/감지 형식 일치 검사가 실패했다.** MP4 계열 형식을 감지한 첫 세그먼트에서 검사가 거부한 뒤 입력 열기가 실패하고 yt-dlp가 exit 1을 반환했다. codec encoder 부족·FPS 입력 계산·영상 존재 여부를 이 오류의 원인으로 단정할 근거는 없다.

설치 실행 파일의 `ffmpeg -hide_banner -h demuxer=hls`를 확인했다. `extension_picky` 기본값은 **true**이고 allowed_extensions·allowed_segment_extensions는 별도 옵션이다. [FFmpeg n8.1 공식 HLS 소스의 test_segment](https://github.com/FFmpeg/FFmpeg/blob/n8.1/libavformat/hls.c#L685)는 extension_picky가 활성일 때 URL 확장자와 감지한 demuxer extension의 일치를 검사하고, 일치하지 않으면 이번과 같은 메시지와 AVERROR_INVALIDDATA를 반환한다. 실행 로그·설치 help·같은 버전 공식 코드가 일치한다.

URL을 제거한 기록만으로 실제 세그먼트 파일명/확장자를 특정하지 않았다. 서버가 잘못된 내용을 반환했는지, naming과 container의 조합 문제인지, 특정 FFmpeg 버전의 호환성 결함인지까지 확정하지 않았다. HTTP/TLS/로그인 장애를 별도 원인으로 추정하지 않는다. 1080p 재현은 수행하지 않았으므로 동일 원인으로 확정하지 않는다.

## 다음 최소 대조 실험

원인 경로를 구체화한 다음 단계는 같은 `hls-3192`/[90,100)초 요청에서 **FFmpeg HLS 입력 옵션 `-extension_picky 0`만 바꿔** 결과를 대조하는 것이다. yt-dlp의 FFmpeg 입력 인자 전달 위치를 먼저 확인한다. 이는 이번 검사에 대한 실험 후보이며 아직 실행하거나 제품 정책에 적용하지 않았다.

- 기본 조건에서 실패가 재현됐으므로 이후 재조회에서도 같은 source/format을 확인하고, 새 출력 위치에서 단일 변경을 비교한다.
- 검사 우회 여부뿐 아니라 실제 output 존재·codec/stream·frame PTS·A/V start/duration·probe/decode를 검사한다. 종료 코드 0만으로 성공을 선언하지 않는다.
- 일반 도구 설정 전체에 검사를 끄거나 `allowed_extensions ALL`을 추가하지 않는다. 적용 범위와 입력 조건은 실험 결과로 정리해야 한다.
- 옵션 변경 후 다른 오류가 생기면 추가 변경을 쌓지 않고 그 오류부터 기록한다. 같은 포맷 획득이 성공해도 CHZZK 전체 포맷/전체 시간축·CFR·정밀 구간 처리가 검증된 것은 아니다.

## 변경·검증 범위

제품 코드·프로젝트·패키지·설치·WPF·ClipEditor·Git staging/commit/tag/branch는 바꾸지 않았다. 임시 진단 프로젝트 빌드 경고/오류 0, 실제 재현 1회·HLS help 조회·공식 소스 대조를 수행했다. 제품 코드 변경이 없어 전체 451개 테스트를 다시 실행하지 않았으며 이번 로그 진단을 새로운 제품 테스트 통과로 기록하지 않는다.

이번 단위는 진단 기록과 안전한 로그를 남기는 커밋 지점이다. 권장 메시지: `docs: Record CHZZK HLS extension validation failure`.

기존 3c-2의 원인 미확인 기록은 당시 상태로 보존한다. 현재는 위 재현 한 건의 직접적인 실패 검사를 확인했다. 수정 실험·1080p 확인·YouTube VP9 header·분리 A/V 시간축·Phase 1 전체 검증은 계속 남아 있다.
