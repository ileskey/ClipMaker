# Metadata fixtures

2026-10-10 실제 metadata-only 조회에서 allowlist로 투영한 JSON이다. yt-dlp 2026.08.19, YouTube는 Node 22.14.0을 명시했다. 영상 다운로드는 하지 않았다.

- CHZZK `15430558`: duration 2667, 미디어 format 5개.
- YouTube `sFXPM_sfJNo`: duration 226, 전체 format 47개 중 양쪽 codec이 none인 storyboard 4개를 제외한 미디어 format 43개.
- title/uploader는 익명 문자열로 대체했다. URL·thumbnail·headers·cookies·signature·원본 title/channel·기타 raw JSON 필드는 보존하지 않는다.
- 보존 필드: id, _type, duration, upload_date, live_status, is_live, has_drm; format의 format_id, width, height, fps, vcodec, acodec, has_drm.
- 숫자의 JSON 값과 per-format stream 정보는 관찰값이다. 포맷 순서 채택·Best 선택·CFR·다운로드 가능성의 검증 자료로 해석하지 않는다.

테스트는 로컬 fixture만 읽으며 네트워크나 설치 도구를 호출하지 않는다.

3c 후속 `selection-chzzk.json`/`selection-youtube.json`은 같은 source 재조회에서 확인한 vbr/abr/protocol만 추가한 정렬 fixture다. 기존 3b fixture는 보존했다. bitrate는 yt-dlp 선언 추정값이며 measured quality가 아니다. title/uploader는 계속 익명화했고 URL·headers·cookies·signature는 없다. 실제 획득/시간축 실패는 3c-2 결과 문서를 따른다.
