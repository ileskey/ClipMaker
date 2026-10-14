# Phase 1 5단계 구현·통합 검증 기록

2026-10-14, `phase/1`, 시작HEAD `241e44a`. 사용자 지시로5단계를 진행한다. 논리 단위 자동 commit을 유지하고 단계 완료 시 통합 보고한다. **현재 누적 작업 기록이며5단계 완료 보고가 아니다.** 6단계 CLI 연결·Phase2/WPF 다운로드 UI/ClipEditor는 진행하지 않는다.

## 5a 출력 workspace/session

기존 정책대로 출력 경로를 준비 시 검증하고 실행 시 재검사한다. Windows reserved names/ADS/제어 문자·MP4 확장자·기존 parent/충돌을 검사한다. Path.GetFullPath가 trailing space/dot를 자동 제거하는 실제 반례를 발견해 원문 spelling부터 거절하도록 보완했다.

확인 후 workspace는 기본 LocalApplicationData/ClipMaker/ClipDownloader/jobs 아래 고유 작업 폴더를 생성하고, 출력 폴더의 고유 staging 경로·파일 ID를 제공한다. staging 파일 자체는 producer의 CreateNew 책임이다. 명시적인 producer resource만 작업 폴더/정확한 staging 범위에 인계하며, 생성 전에 충돌한 staging·사용자 파일·최종 파일을 cleanup 대상으로 추정하지 않는다.

최종 확정은 등록된 검증 staging의 ID/path/bytes/SHA를 확인하고 overwrite=false 이동을 한 번만 시도한다. 이동 전후 content를 대조한다. 반복 commit은 programming error다. cleanup은 취소 token을 받지 않으며 소유 파일만 개별 삭제하고 작업 폴더는 비재귀 empty delete한다. 종료 미확인 보존 모드에서는 삭제하지 않는다. 정리 경고/잔존 경로는 원래 결과와 별도로 전달한다. OperationResult의 실패 metadata 목록도 immutable snapshot으로 추가했다.

13개 실제 local filesystem 테스트로 입력 이름, 기존 최종 파일 충돌, staging bytes 변경, 성공 후 final 유지, 잠긴 파일의 cleanup warning, 미등록 파일 보존, 반복 정리·commit을 검증했다. 초기 테스트의 정리된 parent 아래 없는 파일을 다시 삭제하는 fixture 오류는 수정했다. 이후13개 통과, 관련 Application/Infrastructure build 경고·오류0. root를 주입한 소유 GUID test 폴더를 사용하며 native tool·영상·패키지 설치는 없었다. Application 준비/실행 조정·실제 양 사이트 통합은 다음 단위에서 진행 중이다.

## 파일 닫힘 미확인 인계 보완

5단계 통합 audit에서4단계 collector가 닫힘 실패 후 보존한 파일의 이유를 외부 owner가 알 수 없었던 점을 확인했다. source OwnedMediaFile에 MayBeInUse, shared processing resources에 HasUnconfirmedFileHandles를 추가했다. HLS/indexed/보조 collector와 PCM/FFmpeg 종료 처리에서 이 정보를 보존한다. process 종료 미확인과 file handle 미확인을 구별하여 정상 취소130을 process 실패로 바꾸지 않으면서도 사용 중일 수 있는 파일을 삭제하지 않는다.

기존 indexed/보조 close/close-cancel/delete lifecycle tests에 보존 flag assertion을 추가해 닫힘 미확인만 flag가 켜짐을 검사했다. 현재 Downloader806개와 최신 Media Infrastructure143개를 실제 build/test로 통과했다. 단순 delete 실패는 원래 cleanup 경고로 구별한다. 새 추출 방식·fallback·범용 decoder 준비 조건을 추가하지 않았다.

## 5b 준비와 5c Application 조정

IClipJobService와 immutable ClipRequest/PreparedClip/ClipJobResult/JobProgress를 구현했다. 준비는 기존 URL parser로 요청과 metadata source를 대조하고, 완료 영상/길이·명시 선택·시간 syntax/semantic·출력 경로·필수 도구/기능을 검사한다. 쓰기·workspace/source 작업은 하지 않는다. FPS 미확인에서는 시간 성분과 미확인 검사를 보존한다. 제목/업로더·선택 snapshot·절대 출력·획득 budget을 준비 결과에 고정한다.

실행은 준비 job을 한 번만 소비한다. 출력·도구·metadata를 재검사하며 같은 source/format/선언 조건을 요구한다. Best 재선택·FPS 의미의 조용한 변경·자동 mode/quality fallback은 없다. unknown FPS→검사로 확인한 FPS는 미확인 항목 해소로 구별하여 구간을 검사한다. source inspection/acquisition/verified output의 요청 identity도 대조한다.

취소 callback과 Finalizing 전이는 한 gate에서 조정한다. 전이 전에 수락한 취소는 commit 없이 정리하며, 전이 후의 취소는 commit 결과를 따른다. 반복 실행/commit은 programming error다. 예상 실패·취소는 최종 결과로 반환하며 source와 media registry를 성공/실패/취소 모두 인계한다. 종료 미확인은 Failed·보존·동일 서비스 후속 차단, 파일 닫힘 미확인은 원래 실패/취소 상태를 유지하면서 보존한다. 정리 warning은 원래 결과를 덮지 않는다. cleanup progress callback의 programming error가 나더라도 정리는 finally에서 수행한다.

부분 workspace 생성 후 취소에서 폴더 삭제가 실패하는 경우도 취소 상태를 유지하도록 cancellation metadata를 연결했다. 전체 Application 재시도/파일 접근 재시도는 없다. ClipJobResult는 성공한 최종 경로만 공개하며 cleanup 이후 사라진 입력 경로/processing plan 대신 사용자 원점 기준 요청·actual video/audio 범위와 sample 보정/skip/tail 요약을 제공한다.

20개 조정 테스트에서 확인 전 쓰기 없음, invalid source/time/range/출력/영상 상태·기능 거절, FPS 변경/unknown 해소, 확정 전후 취소, 이동 충돌의 사용자 파일 유지, 종료 미확인·file handle 보존, media 실패, 성공 후 cleanup warning, 부분 workspace 취소 metadata를 검증했다. fake verified media는 orchestration만 검사하며 실제 미디어 성공으로 대신하지 않는다. workspace13개·조정20개·media composition3개 추가, Downloader807개 실패·skip0. 솔루션 build 경고·오류0, Core143/Media Infrastructure143개 실제 실행 통과.
