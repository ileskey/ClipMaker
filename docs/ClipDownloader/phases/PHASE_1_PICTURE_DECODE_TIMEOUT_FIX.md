# Phase 1 후속 — 영상 프레임 해시 검사 시간 정책 보완

2026-10-14. 시작 HEAD `23cb2ae`, branch `phase/1`, clean에서 사용자 CHZZK `14791589` / best·Fast / 46:10~46:40의 `TimedOut/Execution (PresentationVideoProbe)` 보고를 확인했다. 획득 예산은 이전에 검증한 64 MiB / 40초를 유지한다.

## 확인된 결함과 불확실성

`PresentationVideoProbe`는 provider carrier FFprobe 관찰과 FFmpeg 전체 video decode·SHA256 framehash를 같은 실행 helper로 처리했다. helper가 두 명령 모두에 60초 제한을 넣었다. [채택한 시간 정책](PHASE_1_DESIGN.md#412-작업별-제한-시간--사용자-채택-완료)은 버전/기능15초, 로컬 FFprobe60초, 원격 획득·FFmpeg 처리는 고정 제한 없이 사용자 취소를 적용한다. 전체 decoded picture 검증에 FFprobe 제한을 재사용한 것은 이 정책과 불일치했다.

기존 `PresentationVideoProbe` 오류 field는 버전 확인과 FFprobe/FFmpeg 실행 실패를 구별하지 않았다. 따라서 **사용자의 해당 실행이 정확히 어느 프로세스에서 시간 초과했는지는 제공된 출력만으로 확정할 수 없다.** 읽은 native CLI 로그에는 이 사용자 실패 실행 기록이 없고 이전 이 context 성공 실행만 있었다. 사용자 실행의 선택 도구 경로·부하·각 호출 소요 시간도 확인하지 못했다. 사용자 timeout을 현재 호스트에서 직접 재현했다고 기록하지 않는다.

기존에 보존한 동일 CHZZK final에 제품과 같은 FFmpeg framehash 인자를 직접 적용했다. 1920×1080 video1860 frames, stdout208543 bytes, stderr0, exit0, elapsed34.8883535초였다. 입력 SHA256은 `38ce8173b4574f269161f5e7fe7967798148e648cfec9f565562a895f87b3974`다. 이 측정은 한 번의 현재 호스트 결과이며 60초 이내 완료를 보장하거나 사용자 지연 원인을 증명하지 않는다.

## 수정 범위

- FFmpeg 전체 decode/framehash에는 고정 timeout을 전달하지 않고 기존 취소 token을 유지한다.
- FFprobe carrier 관찰60초·도구 버전15초와 중단 후 기존 프로세스/스트림 정리 정책을 유지한다.
- 실행 실패/시간 초과/출력 불완전 field를 버전 `PresentationVideoVersion`, FFprobe `PresentationVideoProbe`, FFmpeg `PresentationVideoDecode`로 구별한다. 종료 불확실은 기존 우선순위대로 `ProcessTerminationFailed`다.
- 모든 picture SHA256·PTS/duration·출력 완전성·stderr·실행 전후 파일 hash 검사는 유지한다. decoder 검사를 생략하거나 화질/모드를 바꾸지 않는다. 새 상한/옵션·패키지/도구 설치는 없다.

FFmpeg 검사에서는 처리 시간이 길어도 60초 경과만으로 실패하지 않는다. 사용자는 기존 Ctrl+C로 중단할 수 있다. 이 변경이 도구 오류·버전 확인 timeout·다른 FFprobe timeout까지 해결한다는 의미는 아니다.

## 검증

솔루션 build 경고/오류0, Downloader Infrastructure850개 실패/skip0을 확인했다. 새/보완 테스트는 버전15초·FFmpeg timeout 없음·carrier FFprobe60초의 분리, 사용자 취소의 null Data, 종료 불확실 우선순위, 실패/출력 불완전 분류·파일 보존을 검사한다. Core/Media suite는 이 후속에서 다시 실행하지 않았다.

같은 사용자 입력을 best/Fast·`--yes`로 실제 CLI 재실행해 exit0과 final 확정을 확인했다. job은 `00722829-e06a-4701-9290-96ea7ce6d416`, 출력은 `.artifacts/chzzk-span-cli/7841c8ef59e342efbf74fc49774a2143/fast.mp4`, 31,798,101 bytes다. SHA256은 위 측정 및 이전 전체 A/V decode를 통과한 파일과 동일하다. source/native·payload/picture·output 검사와 실제 범위 안내를 통과했으며 job 접두사 작업 폴더/staging 수는 각각0이다. 이것은 수정 후 동일 입력의 성공 증거이고 사용자 timeout의 직접 재현 증거가 아니다.

영속 관찰은 [JSON 증거](evidence/PHASE_1_PICTURE_DECODE_TIMEOUT_FIX.json)를 따른다. 기존 사용자 `out.mp4`는 조작하지 않고 별도 고유 ignored 경로를 사용했다. 직접 player의 영상·음성·동기와 Phase1 전체 Passed/Completion/Tag는 계속 Pending이며 Phase2/WPF 다운로드 UI/ClipEditor로 진입하지 않는다.
