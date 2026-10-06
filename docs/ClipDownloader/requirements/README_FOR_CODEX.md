# ClipDownloader — Codex Start Here

Codex는 아래 문서를 순서대로 읽고 작업한다.

1. `01_PRODUCT_REQUIREMENTS.md`
   - 제품의 목적
   - 사용자 시나리오
   - 필요한 기능
   - 지원 범위

2. `02_ARCHITECTURE.md`
   - 기술 스택
   - 프로젝트 구조
   - 핵심 모델/인터페이스
   - 구현 원칙
   - 테스트 전략

3. `03_IMPLEMENTATION_PLAN.md`
   - Phase별 개발 순서
   - Acceptance Criteria
   - Definition of Done

---

## 프로젝트 핵심 요약

ClipDownloader는 C#/.NET/WPF 기반 Windows 애플리케이션이다.

초기 공식 지원:

- CHZZK VOD
- YouTube

핵심 기능:

```text
URL
 ↓
Metadata
 ↓
IN / OUT 지정 (HH:MM:SS:FF)
 ↓
yt-dlp
 ↓
FFmpeg
 ↓
MP4 Clip
```

중요 원칙:

- UI와 yt-dlp / FFmpeg 구현 분리
- async/await
- CancellationToken 지원
- UI freeze 금지
- CHZZK/YouTube 플랫폼 로직을 UI에 직접 넣지 않음
- 향후 키리누키 편집기의 Media Import / Clip Extraction 모듈로 재사용 가능하게 설계

---

## 최초 작업

처음에는 `03_IMPLEMENTATION_PLAN.md`의 **Phase 0**부터 시작한다.

한 Phase를 완료하기 전 다음 Phase로 넘어가지 않는다.
