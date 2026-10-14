# Phase 1 후속 — CHZZK 소리 있는 Accurate 검증 자료

2026-10-15 (Asia/Seoul), 시작 HEAD `f545d65`, branch `phase/1`. 사용자가15537558·00:00:45~를 제공했다. 끝 시간 생략에 대해45~55초·10초 Accurate로 먼저 검증한다고 안내했다. 기존 bounded 다운로드·자동 논리 커밋 권한과64MiB/60초 예산을 적용하고 사용자 파일을 보존했다.

## 실제 생성과 자동 검증

기존 설치 yt-dlp2026.08.19/FFmpeg·FFprobe8.1을 사용한 실제CLI가 exit0으로최종확정에성공했다. 메타데이터·원본시작참조획득·native/codec/시간검사·Accurate decoder 준비/PCM/render·output gate·정리까지통과했다. 도구설치·제품코드변경·예산확대·다른mode/quality전환은없다.

- Source15537558, best hls-8384,1080p/60fps,H.264/AAC.
- 요청 `[45,55)`초.
- 실제video `[432013/9600,528013/9600)`초, audio `[45,55)`초.
- source sample-grid/시간축검증범위에서zero1 sample삽입·overlap2 sample제거,AAC skip1024/tail256.
- SourceEndVerified=false.
- job `3fdb09c6-0a49-4286-aa10-a8f01bec8939`.
- final `.artifacts/chzzk-audible-accurate-cli/fc159febe0be4bdd91610c046254bb7f/accurate.mp4`,2883050bytes·SHA256 `ef45e221e887c29a3eff11ba92d56515c780d9fbd8e918900ea4c8384fd03b2c`.

독립 FFmpeg 전체 A/V decode exit0과 video framehash exit0을 확인했다. 1920x1080·1/60 clock·600frames이며 600개의 decoded picture hash가 서로 달랐다. 음성 volumedetect exit0, mean −34.3dB / max −11.4dB로 완전 무음이 아닌 signal을 확인했다. 음성 내용의 청취·사람이 인식하는 움직임·A/V 동기의 직접 평가와 구별한다.

실제 native job root에서 해당 job ID 접두사의 작업 folder0, 출력 folder staging0을 확인했다. 별도 진단 파일과 사용자 `out*.mp4`는 조작하지 않았다. stdout/stderr/summary/framehash는 위 ignored 출력 folder에 보존하고 안전한 영속 값은 [JSON 증거](evidence/PHASE_1_CHZZK_AUDIBLE_ACCURATE_VERIFICATION.json)에 기록했다. signed URL·headers·metadata 원문은 문서에 기록하지 않는다.

## 完了条件

같은 source/format/범위의 Accurate 생성·자동 검증은 완료했다. 새 제품 구현은 없고 build/test는 재실행하지 않았다. 직전 제품 검증은 build 경고/오류0·Downloader859개 통과이며 Core143/Media193은 그 이전의 기록으로 구분한다.

사용자가 보존 final의 재생·음성 동기를 확인하는 단계는 Pending이다. 이전 무음 CHZZK 자료를 대신할 검증 후보로 전달한다. YouTube 양 mode와 CHZZK Fast의 사용자 통과 기록은 보존하되 이번 자동 성공을 CHZZK Accurate의 사람 검증이나 Phase1 전체 Passed/Completion/Tag로 승격하지 않는다. MPD metadata 문제·긴 playlist·46분 Accurate는 별도 미지원 항목이며 Phase2/WPF 다운로드 UI/ClipEditor는 진행하지 않는다.
