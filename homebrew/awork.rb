# Homebrew formula for awork
# To use: copy to awork-io/homebrew-tap repo as Formula/awork.rb

class Awork < Formula
  desc "Token-only, swagger-driven CLI for awork"
  homepage "https://github.com/awork-io/awork-cli"
  version "0.1.0"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/awork-io/awork-cli/releases/download/v#{version}/awork-osx-arm64.tar.gz"
      sha256 "PLACEHOLDER_SHA256_ARM64"
    end
    on_intel do
      url "https://github.com/awork-io/awork-cli/releases/download/v#{version}/awork-osx-x64.tar.gz"
      sha256 "PLACEHOLDER_SHA256_X64"
    end
  end

  on_linux do
    url "https://github.com/awork-io/awork-cli/releases/download/v#{version}/awork-linux-x64.tar.gz"
    sha256 "PLACEHOLDER_SHA256_LINUX"
  end

  def install
    bin.install "awork"
  end

  test do
    assert_match "awork", shell_output("#{bin}/awork --version", 2)
  end
end
