// Simple sentiment analysis example for code generation testing

enum Sentiment {
  Happy
  Sad
  Angry
  Neutral
}

class AnalysisResult {
  sentiment Sentiment
  confidence float
  reasoning string
}

function AnalyzeSentiment(text: string) -> AnalysisResult {
  client "openai/gpt-4o-mini"
  prompt #"
    Analyze the sentiment of this text:
    {{ text }}

    {{ ctx.output_format }}
  "#
}

// Simple test
test positive_sentiment {
  functions [AnalyzeSentiment]
  args {
    text "I love this amazing product!"
  }
}
