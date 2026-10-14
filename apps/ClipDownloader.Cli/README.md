# ClipDownloader CLI

Phase 1의 별도 `net10.0` 호스트다. WPF를 참조하지 않으며 `diagnose`, `info`, `clip`을 제공한다.

```powershell
dotnet build ClipMaker.slnx
dotnet run --project apps/ClipDownloader.Cli --no-build -- --help
dotnet run --project apps/ClipDownloader.Cli --no-build -- diagnose
dotnet run --project apps/ClipDownloader.Cli --no-build -- info --url "https://chzzk.naver.com/video/15430558"
dotnet run --project apps/ClipDownloader.Cli --no-build -- clip --url "https://chzzk.naver.com/video/15430558" --start 00:00:10:00 --end 00:00:20:00 --output .\clip.mp4
```

현재 디렉터리 기준 기존 출력 폴더와 MP4 파일명이 필요하다. 기존 파일을 덮어쓰지 않는다. 기본 화질/모드는 `best`/`fast`이며 `--quality 720p`, `--mode accurate`처럼 지정한다. 실제 지원은 [4단계의 검증된 범위와 제한](../../docs/ClipDownloader/phases/PHASE_1_STAGE_4_VERIFICATION.md)을 따른다. 메타데이터의 화질 선택 가능 여부가 획득·처리 지원을 보증하지 않는다.

도구 경로는 `--yt-dlp-path`, `--ffmpeg-path`, `--ffprobe-path`로 각각 지정한다. 생략하면 기존 사용자 설정/호스트 `tools`/PATH 탐색 정책을 따른다. CLI는 설정 파일을 추가하거나 설치 경로를 자동 발견하지 않는다. YouTube는 기존 탐색 범위의 Node.js 22+와 yt-dlp의 사용 가능한 EJS가 필요하며 자동 설치·원격 구성 요소 다운로드를 하지 않는다.

`clip`은 준비 요약과 미확인 검사를 표시한 뒤 `위 설정으로 클립을 생성하시겠습니까? [y/N]`으로 확인한다. Y만 진행하고 N·빈 입력·공백·EOF는 취소한다. 다른 답은 재질문한다. stdin이 리디렉션된 실행에는 `--yes`가 필요하다. `--yes`는 같은 요약을 표시하고 확인 입력만 생략하며 검증은 유지한다. 확인 전 작업 폴더·출력 파일·원격 구간을 만들지 않는다. 진단 로그는 별도로 기록한다.

원본 참조 획득 상한은 기존 실제 검증의 **32 MiB / 30초**다. Accurate에서 필요한 원본 시작 참조도 상한에 포함되므로 모든 요청 위치가 지원되는 것은 아니다. 상한을 넘는 요청은 실패하며 전체 다운로드·mode/quality 변경으로 대체하지 않는다.

정상 정보·준비 요약·최종 결과는 stdout, 질문·단계·주의사항·오류·잔존 경로는 stderr다. 리디렉션된 stderr는 단계 전환만 기록한다. 실제 영상/음성 표시 범위는 사용자 원점의 정확한 유리수 초와 반열린 구간으로 표시하며 요청 범위와 다를 수 있다.

Ctrl+C는 취소와 자원 정리를 요청한다. 반복 입력으로 정리를 생략하지 않는다. Finalizing 진입 이후의 늦은 취소는 최종 확정 결과를 따른다. 종료 코드는 성공 `0`, 실행 오류 `1`, 입력 오류 `2`, 취소 `130`이다. 파일 정리 경고와 잔존 경로는 원래 결과를 덮지 않는다.

로그는 `%LOCALAPPDATA%\ClipMaker\ClipDownloader\Logs\clipdownloader-cli-*.log`에 일별/10 MiB 회전과 14개 보존으로 기록한다. 명령 이름·작업 ID·단계·최종 종료 코드만 기록하며 입력 URL, 원시 JSON, 도구 인자, 인증값, 서명 주소를 복제하지 않는다. 로그 실패는 stderr 경고로 알리고 작업 결과는 유지한다. 명시적인 작업 정리가 끝난 뒤 DI owner와 로그를 종료한다.
