# Phase 1 5단계 구현·통합 검증 기록

2026-10-14, `phase/1`, 시작HEAD `241e44a`. 사용자 지시로5단계를 진행한다. 논리 단위 자동 commit을 유지하고 단계 완료 시 통합 보고한다. **현재 누적 작업 기록이며5단계 완료 보고가 아니다.** 6단계 CLI 연결·Phase2/WPF 다운로드 UI/ClipEditor는 진행하지 않는다.

## 5a 출력 workspace/session

기존 정책대로 출력 경로를 준비 시 검증하고 실행 시 재검사한다. Windows reserved names/ADS/제어 문자·MP4 확장자·기존 parent/충돌을 검사한다. Path.GetFullPath가 trailing space/dot를 자동 제거하는 실제 반례를 발견해 원문 spelling부터 거절하도록 보완했다.

확인 후 workspace는 기본 LocalApplicationData/ClipMaker/ClipDownloader/jobs 아래 고유 작업 폴더를 생성하고, 출력 폴더의 고유 staging 경로·파일 ID를 제공한다. staging 파일 자체는 producer의 CreateNew 책임이다. 명시적인 producer resource만 작업 폴더/정확한 staging 범위에 인계하며, 생성 전에 충돌한 staging·사용자 파일·최종 파일을 cleanup 대상으로 추정하지 않는다.

최종 확정은 등록된 검증 staging의 ID/path/bytes/SHA를 확인하고 overwrite=false 이동을 한 번만 시도한다. 이동 전후 content를 대조한다. 반복 commit은 programming error다. cleanup은 취소 token을 받지 않으며 소유 파일만 개별 삭제하고 작업 폴더는 비재귀 empty delete한다. 종료 미확인 보존 모드에서는 삭제하지 않는다. 정리 경고/잔존 경로는 원래 결과와 별도로 전달한다. OperationResult의 실패 metadata 목록도 immutable snapshot으로 추가했다.

13개 실제 local filesystem 테스트로 입력 이름, 기존 최종 파일 충돌, staging bytes 변경, 성공 후 final 유지, 잠긴 파일의 cleanup warning, 미등록 파일 보존, 반복 정리·commit을 검증했다. 초기 테스트의 정리된 parent 아래 없는 파일을 다시 삭제하는 fixture 오류는 수정했다. 이후13개 통과, 관련 Application/Infrastructure build 경고·오류0. root를 주입한 소유 GUID test 폴더를 사용하며 native tool·영상·패키지 설치는 없었다. Application 준비/실행 조정·실제 양 사이트 통합은 다음 단위에서 진행 중이다.
