class DongMcu < Formula
  desc "Claude 사용량을 화면 위에 항상 띄워두는 macOS HUD"
  homepage "https://github.com/ldg030201/dong-mcu"
  url "https://github.com/ldg030201/dong-mcu/archive/refs/tags/v0.1.2.tar.gz"
  sha256 "9545d0cf9f2b5fa5ddc9f7d148c29544a8a06c6db0bc88987747b51f938f6dcf"
  license "MIT"
  head "https://github.com/ldg030201/dong-mcu.git", branch: "main"

  depends_on macos: :sonoma

  def install
    # Homebrew의 빌드 샌드박스 안에서는 SwiftPM이 매니페스트를 평가할 때 쓰는
    # 자체 샌드박스가 중첩되어 실패한다.
    ENV["SWIFT_BUILD_FLAGS"] = "--disable-sandbox"
    system "./build.sh"
    prefix.install "build/dong-mcu.app"
    bin.install_symlink prefix/"dong-mcu.app/Contents/MacOS/dong-mcu"
  end

  def caveats
    <<~EOS
      dong-mcu는 Claude Code가 keychain에 저장한 OAuth 토큰을 읽습니다.
      Claude Code에 로그인되어 있어야 동작하고, 첫 실행 때 keychain 접근 허용을 한 번 묻습니다.

      실행:
        open #{opt_prefix}/dong-mcu.app

      Launchpad와 /Applications 에서 보이게 하려면:
        ln -sfn #{opt_prefix}/dong-mcu.app /Applications/dong-mcu.app

      로그인하면 자동으로 시작하게 하려면 시스템 설정 > 일반 > 로그인 항목에 추가하세요.
    EOS
  end

  test do
    assert_match "dong-mcu", shell_output("#{bin}/dong-mcu --version")
  end
end
