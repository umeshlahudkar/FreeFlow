using FreeFlow.Enums;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace FreeFlow.GamePlay
{
    /// <summary>
    /// Represents a game block, single unit on the game board
    /// </summary>
    public class Block : MonoBehaviour
    {
        [SerializeField] private Image pairDotImage;
        [SerializeField] private Image[] directionImages;

        private int row_ID;
        private int coloum_ID;

        private bool isPairBlock;

        private PairColorType pairColorType;
        private PairColorType highlightedColorType;

        /// <summary>
        /// Sets the properties of the block, including its position, pair color type,
        /// </summary>
        /// <param name="type">The pair color type of the block.</param>
        /// <param name="rowIndex">The row index of the block.</param>
        /// <param name="coloumIndex">The column index of the block.</param>
        public void SetBlock(PairColorType type, int rowIndex, int coloumIndex)
        {
            this.row_ID = rowIndex;
            this.coloum_ID = coloumIndex;
            pairColorType = type;

            if (pairColorType != PairColorType.None)
            {
                isPairBlock = true;
                pairDotImage.gameObject.SetActive(true);
                pairDotImage.color = GamePlayController.Instance.GetColor(type);

                pairDotImage.transform.localScale = Vector3.zero;
                pairDotImage.transform.DOScale(1, 0.5f);
            }
        }

        /// <summary>
        /// Highlights the block in a specified direction with a given pair color type.
        /// </summary>
        /// <param name="dir">The direction in which to highlight the block.</param>
        /// <param name="type">The pair color type used for the highlight color.</param>
        public void HighlightBlockDirection(Direction dir, PairColorType type)
        {
            highlightedColorType = type;
            directionImages[((int)dir - 1)].gameObject.SetActive(true);
            directionImages[((int)dir - 1)].color = GamePlayController.Instance.GetColor(type);
        }

        /// <summary>
        /// Resets all highlight directions of the block.
        /// </summary>
        public void ResetAllHighlightDirection()
        {
            for (int i = 0; i < directionImages.Length; i++)
            {
                directionImages[i].gameObject.SetActive(false);
            }

            highlightedColorType = PairColorType.None;
        }

        /// <summary>
        /// Resets the highlight direction of the block in a specific direction.
        /// </summary>
        /// <param name="dir">The direction to reset the highlight for.</param>
        public void ResetHighlightDirection(Direction dir)
        {
            directionImages[((int)dir - 1)].gameObject.SetActive(false);
        }

        public void HighlightBlock()
        {
            pairDotImage.transform.DOScale(1.3f, 0.5f);
        }

        public void ResetHighlightBlock()
        {
            pairDotImage.transform.DOScale(1f, 0.5f);
        }

        public bool IsPairBlock
        {
            get { return isPairBlock; }
        }

        public PairColorType PairColorType
        {
            get { return pairColorType; }
        }

        public PairColorType HighlightedColorType
        {
            get { return highlightedColorType; }
        }

        public int Row_ID { get { return row_ID; } }
        public int Coloum_ID { get { return coloum_ID; } }

        /// <summary>
        /// Resets all properties of the block to their default values.
        /// </summary>
        public void ResetBlock()
        {
            this.row_ID = -1;
            this.coloum_ID = -1;
            pairColorType = PairColorType.None;
            highlightedColorType = PairColorType.None;
            isPairBlock = false;
            pairDotImage.transform.localScale = Vector3.zero;

            pairDotImage.gameObject.SetActive(false);
            ResetAllHighlightDirection();
        }
    }
}